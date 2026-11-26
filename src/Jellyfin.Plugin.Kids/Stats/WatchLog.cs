using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// The watch log (feature 22, "advanced statistics"): turns the same counted playing time as the daily allowance into
/// segments of one item on one device, and keeps breaks. Records only while <see cref="IKidsSettings.AdvancedStats"/>
/// is on.
/// </summary>
/// <remarks>
/// <para>Segment rules (spec 022, R2): a segment is one item played in one playback (session and play session). It
/// ends when the playback stops, the item changes, a new household day starts, nothing was counted for
/// <see cref="SegmentGap"/> (a long pause) or the playback goes idle (<see cref="PlaybackClock.IdleTimeout"/>). A seek
/// or a short pause keeps the segment. Two clients make two segments. Its seconds are exactly the counted time, so a
/// day's segments add up to that day's screen time from the server's events.</para>
/// <para>Called from the playback events under the tracker's lock: memory only, no I/O. Changed segments and breaks are
/// written by <see cref="FlushAsync"/> with the usage flush, a growing segment upserted by its id, so a crash loses at
/// most one flush interval. Rows of users who are no longer kids are dropped at the flush.</para>
/// </remarks>
public sealed class WatchLog : IDisposable
{
    /// <summary>
    /// The most ended segments, and the most breaks, kept in memory while they cannot be written. Past it the oldest
    /// are dropped, so a database that keeps failing cannot grow memory without end.
    /// </summary>
    public const int MaxPending = 10_000;

    /// <summary>How long nothing may be counted in a playback before the next counted time starts a new segment.</summary>
    public static readonly TimeSpan SegmentGap = TimeSpan.FromMinutes(2);

    private readonly StatsStore _store;
    private readonly KidRoster _roster;
    private readonly IKidsSettings _settings;
    private readonly ILogger<WatchLog> _logger;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly Dictionary<(string SessionId, string PlaySessionId), Segment> _open = [];
    private readonly List<Segment> _closed = [];
    private readonly Dictionary<(Guid UserId, DateTimeOffset StartedAt), (BreakRecord Break, int Version)> _breaks = [];
    private int _breakVersion;
    private bool _flushFailing;
    private int _dropped;
    private bool _droppedLogged;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchLog"/> class.
    /// </summary>
    /// <param name="store">The statistics store.</param>
    /// <param name="roster">The kid ids, so a former kid's rows are not written.</param>
    /// <param name="settings">The household settings.</param>
    /// <param name="logger">The logger.</param>
    public WatchLog(StatsStore store, KidRoster roster, IKidsSettings settings, ILogger<WatchLog> logger)
    {
        _store = store;
        _roster = roster;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the watch log is recorded now: on, and the database not known unusable.</summary>
    public bool Enabled => _settings.AdvancedStats && !_store.IsKnownUnusable;

    /// <summary>Gets how many ended segments and breaks wait to be written.</summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _closed.Count + _breaks.Count;
            }
        }
    }

    /// <summary>Gets how many segments are open.</summary>
    public int OpenCount
    {
        get
        {
            lock (_lock)
            {
                return _open.Count;
            }
        }
    }

    /// <summary>
    /// Adds counted playing time of a playback. No I/O. Does nothing while the watch log is off.
    /// </summary>
    /// <param name="sessionId">The Jellyfin session.</param>
    /// <param name="playSessionId">The play session, or null.</param>
    /// <param name="userId">The kid.</param>
    /// <param name="item">What plays.</param>
    /// <param name="device">The device name.</param>
    /// <param name="client">The app name.</param>
    /// <param name="from">The start of the counted interval.</param>
    /// <param name="to">The end of the counted interval.</param>
    public void Played(string sessionId, string? playSessionId, Guid userId, WatchItem item, string? device, string? client, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Enabled || to <= from)
        {
            return;
        }

        var key = (sessionId, playSessionId ?? string.Empty);
        var clock = Clock();
        lock (_lock)
        {
            var cursor = from;
            foreach (var (day, length) in clock.Split(from, to))
            {
                var partFrom = cursor;
                var partTo = cursor + length;
                cursor = partTo;
                var dayKey = DayClock.Format(day);
                string? playbackId = null;
                if (_open.TryGetValue(key, out var segment)
                    && (segment.Item != item || segment.Day != dayKey || partFrom - segment.EndedAt >= SegmentGap))
                {
                    // The same item goes on in the same playback: a new day or a long pause, not a new play
                    playbackId = segment.Item == item ? segment.PlaybackId : null;
                    Close(key, segment);
                    segment = null;
                }

                if (segment is null)
                {
                    segment = new Segment(NewId(), playbackId ?? NewId(), userId, dayKey, partFrom, item, device, client);
                    _open[key] = segment;
                }

                segment.Milliseconds += (long)length.TotalMilliseconds;
                if (partTo > segment.EndedAt)
                {
                    segment.EndedAt = partTo;
                }

                segment.Version++;
            }
        }
    }

    /// <summary>
    /// Ends a playback's segment. No I/O.
    /// </summary>
    /// <param name="sessionId">The Jellyfin session.</param>
    /// <param name="playSessionId">The play session, or null.</param>
    public void Stopped(string sessionId, string? playSessionId)
    {
        var key = (sessionId, playSessionId ?? string.Empty);
        lock (_lock)
        {
            if (_open.TryGetValue(key, out var segment))
            {
                Close(key, segment);
            }
        }
    }

    /// <summary>
    /// Ends the segments of playbacks with nothing counted for <see cref="PlaybackClock.IdleTimeout"/>.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>How many were ended.</returns>
    public int Sweep(DateTimeOffset now)
    {
        lock (_lock)
        {
            var idle = _open.Where(o => now - o.Value.EndedAt >= PlaybackClock.IdleTimeout).ToList();
            foreach (var (key, segment) in idle)
            {
                Close(key, segment);
            }

            return idle.Count;
        }
    }

    /// <summary>
    /// Records a break that started or that a grown-up ended. No I/O. Does nothing while the watch log is off.
    /// </summary>
    /// <param name="notice">The break.</param>
    public void OnBreak(BreakNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (!Enabled)
        {
            return;
        }

        var minutes = (int)Math.Round(Math.Max(0, notice.Length.TotalMinutes), MidpointRounding.AwayFromZero);
        var record = new BreakRecord(notice.Kid, DayClock.Format(Clock().DayOf(notice.StartedAt)), notice.StartedAt, minutes, notice.EndedByGrownUp);
        lock (_lock)
        {
            _breaks[(notice.Kid, notice.StartedAt)] = (record, ++_breakVersion);
            if (_breaks.Count > MaxPending)
            {
                _breaks.Remove(_breaks.MinBy(b => b.Value.Version).Key);
                _dropped++;
            }
        }
    }

    /// <summary>
    /// Writes the changed segments and breaks in one transaction. What changes while the write runs stays for the next
    /// flush.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if everything was written; <c>false</c> if the write failed and it is kept.</returns>
    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<(Segment Segment, int Version, WatchSegment Row)> segments;
            List<(BreakRecord Break, int Version)> breaks;
            lock (_lock)
            {
                segments = _open.Values.Concat(_closed)
                    .Where(s => s.Version != s.WrittenVersion)
                    .Select(s => (s, s.Version, s.ToRow()))
                    .ToList();
                breaks = [.. _breaks.Values];
            }

            if (segments.Count == 0 && breaks.Count == 0)
            {
                return true;
            }

            // A former kid's rows are dropped, never written back after a purge
            var known = _roster.IsLoaded;
            var rows = segments.Where(s => !known || _roster.Contains(s.Row.UserId)).Select(s => s.Row).ToList();
            var breakRows = breaks.Where(b => !known || _roster.Contains(b.Break.UserId)).Select(b => b.Break).ToList();
            try
            {
                if (rows.Count > 0 || breakRows.Count > 0)
                {
                    await _store.WriteAsync(rows, breakRows, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!_flushFailing)
                {
                    _flushFailing = true;
                    _logger.LogWarning(ex, "Could not store the kids watch log; keeping {Count} pending row(s)", rows.Count + breakRows.Count);
                }

                if (!_droppedLogged && DroppedSoFar() > 0)
                {
                    _droppedLogged = true;
                    _logger.LogWarning("The kids watch log still cannot be stored; dropping the oldest rows past {Max} pending", MaxPending);
                }

                return false;
            }

            int dropped;
            lock (_lock)
            {
                dropped = _dropped;
                _dropped = 0;
            }

            _droppedLogged = false;
            if (_flushFailing || dropped > 0)
            {
                _flushFailing = false;
                _logger.LogInformation("Stored the kids watch log again; {Dropped} older row(s) were dropped while it could not be stored", dropped);
            }

            lock (_lock)
            {
                foreach (var (segment, version, _) in segments)
                {
                    segment.WrittenVersion = version;
                }

                _closed.RemoveAll(s => s.WrittenVersion == s.Version);
                foreach (var (record, version) in breaks)
                {
                    var key = (record.UserId, record.StartedAt);
                    if (_breaks.TryGetValue(key, out var current) && current.Version == version)
                    {
                        _breaks.Remove(key);
                    }
                }
            }

            return true;
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// Deletes the whole watch log, stored and in memory, for the dashboard's Delete button. Serialised with flushes,
    /// so a flush in progress cannot write it back; playbacks going on start new segments.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_lock)
            {
                _open.Clear();
                _closed.Clear();
                _breaks.Clear();
            }

            return await _store.DeleteWatchLogAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _flushLock.Dispose();

    private static string NewId() => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private DayClock Clock() => new(_settings.Zone, _settings.DayStartsAtHour);

    private int DroppedSoFar()
    {
        lock (_lock)
        {
            return _dropped;
        }
    }

    private void Close((string SessionId, string PlaySessionId) key, Segment segment)
    {
        _open.Remove(key);
        if (segment.Version != segment.WrittenVersion)
        {
            _closed.Add(segment);
            if (_closed.Count > MaxPending)
            {
                _closed.RemoveAt(0);
                _dropped++;
            }
        }
    }

    private sealed class Segment(string id, string playbackId, Guid userId, string day, DateTimeOffset startedAt, WatchItem item, string? device, string? client)
    {
        public string PlaybackId { get; } = playbackId;

        public string Day { get; } = day;

        public WatchItem Item { get; } = item;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public DateTimeOffset EndedAt { get; set; } = startedAt;

        public long Milliseconds { get; set; }

        public int Version { get; set; }

        public int WrittenVersion { get; set; }

        public WatchSegment ToRow()
            => new(id, PlaybackId, userId, Day, StartedAt, EndedAt, (int)((Milliseconds + 500) / 1000), Item, device, client);
    }
}
