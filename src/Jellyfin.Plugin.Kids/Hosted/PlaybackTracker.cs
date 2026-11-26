using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Stats;
using Jellyfin.Plugin.Kids.Storage;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Hosted;

/// <summary>
/// Counts kids' screen time from Jellyfin's playback events, whichever client plays (research R1–R3).
/// </summary>
/// <remarks>
/// The session manager raises the events synchronously, from client requests and from each session's one-second
/// automatic-progress timer, so the handlers only update memory under one lock: no I/O, and no exception ever reaches
/// the session manager. The kid check and the event time are both taken inside the lock, so events are applied in the
/// order they are timed and none is counted for a user unmarked meanwhile. Only the user who owns the
/// session is counted, and only if they are a kid. A timer flushes the counted time every 30 seconds.
/// </remarks>
public sealed class PlaybackTracker : IHostedService, IDisposable
{
    /// <summary>How often counted time is stored.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    private readonly ISessionManager _sessions;
    private readonly KidRoster _roster;
    private readonly KidProfileService _profiles;
    private readonly KidsDatabase _database;
    private readonly UsageService _usage;
    private readonly WatchLog _watch;
    private readonly EnforcementState _enforcement;
    private readonly PlaybackEnforcer _enforcer;
    private readonly TimeProvider _time;
    private readonly ILogger<PlaybackTracker> _logger;
    private readonly Lock _lock = new();
    private readonly PlaybackClock _clock = new();
    private ITimer? _timer;
    private bool _rosterFailureLogged;
    private bool _watchFailureLogged;
    private int _ticking;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackTracker"/> class.
    /// </summary>
    /// <param name="sessions">Jellyfin's session manager.</param>
    /// <param name="roster">The kid user ids.</param>
    /// <param name="profiles">The kid profiles, to load the roster.</param>
    /// <param name="database">The plugin database.</param>
    /// <param name="usage">The screen-time service.</param>
    /// <param name="watch">The watch log (feature 22), fed with the same counted time while it is on.</param>
    /// <param name="enforcement">The kids' limits in memory, fed with the same counted time.</param>
    /// <param name="enforcer">Acts on kids' sessions when a limit is near or reached.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackTracker(
        ISessionManager sessions,
        KidRoster roster,
        KidProfileService profiles,
        KidsDatabase database,
        UsageService usage,
        WatchLog watch,
        EnforcementState enforcement,
        PlaybackEnforcer enforcer,
        TimeProvider time,
        ILogger<PlaybackTracker> logger)
    {
        _sessions = sessions;
        _roster = roster;
        _profiles = profiles;
        _database = database;
        _usage = usage;
        _watch = watch;
        _enforcement = enforcement;
        _enforcer = enforcer;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadRosterAsync(cancellationToken).ConfigureAwait(false);
        _sessions.PlaybackStart += OnPlaybackStart;
        _sessions.PlaybackProgress += OnPlaybackProgress;
        _sessions.PlaybackStopped += OnPlaybackStopped;
        _enforcement.BreakChanged += OnBreakChanged;
        _timer = _time.CreateTimer(_ => _ = TickAsync(), null, FlushInterval, FlushInterval);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackStart -= OnPlaybackStart;
        _sessions.PlaybackProgress -= OnPlaybackProgress;
        _sessions.PlaybackStopped -= OnPlaybackStopped;
        _enforcement.BreakChanged -= OnBreakChanged;
        _timer?.Dispose();
        _timer = null;
        await FlushSafeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _timer?.Dispose();

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e) => Handle(PlaybackEventKind.Start, e);

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e) => Handle(PlaybackEventKind.Progress, e);

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (Handle(PlaybackEventKind.Stop, e))
        {
            // Stored soon after a stop, without blocking the session manager (FR-007)
            _ = Task.Run(FlushSafeAsync);
        }
    }

    private bool Handle(PlaybackEventKind kind, PlaybackProgressEventArgs e)
    {
        try
        {
            // The owner only, never e.Users, which includes additional users (clarification Q3)
            var session = e.Session;
            if (session is null)
            {
                return false;
            }

            lock (_lock)
            {
                if (!_roster.Contains(session.UserId))
                {
                    return false;
                }

                var playbackEvent = new PlaybackEvent(
                    kind,
                    session.Id,
                    e.PlaySessionId,
                    session.UserId,
                    e.IsPaused,
                    kind == PlaybackEventKind.Progress && e.IsAutomated,
                    _time.GetUtcNow());
                var counted = _clock.Apply(playbackEvent);
                foreach (var (userId, from, to) in counted)
                {
                    _usage.AddPlayed(userId, from, to);
                    _enforcement.OnPlayed(userId, from, to);
                }

                AddToWatchLog(kind, e, session, counted);
            }

            // Decided in memory; any action runs off the session manager's thread
            if (kind != PlaybackEventKind.Stop)
            {
                _ = _enforcer.OnPlayback(KidSession.From(session), kind == PlaybackEventKind.Progress && e.IsAutomated);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not count a playback event for kids screen time");
            return false;
        }
    }

    // The watch log reads the item from memory only, and only while it is on. Its own catch: a failure here never
    // stops the screen time count or enforcement for the same event
    private void AddToWatchLog(PlaybackEventKind kind, PlaybackProgressEventArgs e, SessionInfo session, IReadOnlyList<(Guid UserId, DateTimeOffset From, DateTimeOffset To)> counted)
    {
        try
        {
            if (_watch.Enabled && counted.Count > 0)
            {
                var item = WatchItem.From(e.Item);
                var device = session.DeviceName ?? e.DeviceName;
                var client = session.Client ?? e.ClientName;
                foreach (var (userId, from, to) in counted)
                {
                    _watch.Played(session.Id, e.PlaySessionId, userId, item, device, client, from, to);
                }
            }

            // Also while off, so a segment open when it was turned off ends at the stop
            if (kind == PlaybackEventKind.Stop)
            {
                _watch.Stopped(session.Id, e.PlaySessionId);
            }
        }
        catch (Exception ex)
        {
            if (!_watchFailureLogged)
            {
                _watchFailureLogged = true;
                _logger.LogWarning(ex, "Could not add a playback event to the kids watch log; screen time is still counted");
            }
            else
            {
                _logger.LogDebug(ex, "Could not add a playback event to the kids watch log");
            }
        }
    }

    private async Task TickAsync()
    {
        // A slow database must not stack ticks up
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            if (!_roster.IsLoaded)
            {
                await LoadRosterAsync(CancellationToken.None).ConfigureAwait(false);
            }

            lock (_lock)
            {
                var now = _time.GetUtcNow();
                _clock.Sweep(now);
                _watch.Sweep(now);
            }

            // Denied kids' sessions still shown as playing without reporting
            _ = _enforcer.Sweep();
            await FlushSafeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kids screen time timer failed");
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    private async Task LoadRosterAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_database.EnsureInitialized())
            {
                await _profiles.LoadRosterAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not load the kids roster");
        }

        if (_roster.IsLoaded)
        {
            if (_rosterFailureLogged)
            {
                _logger.LogInformation("Kids screen time is counted again: the kids database can be read");
                _rosterFailureLogged = false;
            }
        }
        else if (!_rosterFailureLogged)
        {
            _rosterFailureLogged = true;
            _logger.LogWarning("Kids screen time is not counted until the kids database can be read");
        }
    }

    private async Task FlushSafeAsync()
    {
        try
        {
            await _usage.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not store kids screen time");
        }

        try
        {
            await _watch.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not store the kids watch log");
        }
    }

    private void OnBreakChanged(object? sender, GenericEventArgs<BreakNotice> e)
    {
        try
        {
            _watch.OnBreak(e.Argument);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not note a kids break in the watch log");
        }
    }
}
