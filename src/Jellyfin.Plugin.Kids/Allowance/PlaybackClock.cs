using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// Turns playback events into playing time (research R2): wall-clock time between two events of the same playback,
/// only while it was playing, at most <see cref="MaxGap"/> per gap, and no later than <see cref="RealReportCutOff"/>
/// after the client's last real report.
/// </summary>
/// <remarks>Not thread-safe: the caller serialises calls.</remarks>
public sealed class PlaybackClock
{
    /// <summary>The most time counted from one gap between events.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);

    /// <summary>How long synthesised progress keeps counting after the client's last real report.</summary>
    public static readonly TimeSpan RealReportCutOff = TimeSpan.FromSeconds(60);

    /// <summary>How long a playback with no event is kept before the sweep removes it.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(6);

    private readonly Dictionary<(string SessionId, string PlaySessionId), Record> _records = [];

    /// <summary>
    /// Gets how many playbacks are being followed.
    /// </summary>
    public int Count => _records.Count;

    /// <summary>
    /// Applies an event.
    /// </summary>
    /// <param name="playbackEvent">The event.</param>
    /// <returns>The wall-clock intervals to count, with the user to count them for.</returns>
    public IReadOnlyList<(Guid UserId, DateTimeOffset From, DateTimeOffset To)> Apply(PlaybackEvent playbackEvent)
    {
        ArgumentNullException.ThrowIfNull(playbackEvent);
        var key = (playbackEvent.SessionId, playbackEvent.PlaySessionId ?? string.Empty);
        var at = playbackEvent.At;
        if (!_records.TryGetValue(key, out var record))
        {
            // A start, or a playback that began before the plugin or before the user became a kid: count from now
            if (playbackEvent.Kind != PlaybackEventKind.Stop)
            {
                _records[key] = new Record(playbackEvent.OwnerUserId, at, at, !playbackEvent.IsPaused);
            }

            return [];
        }

        if (playbackEvent.Kind == PlaybackEventKind.Stop)
        {
            _records.Remove(key);
        }

        // Out of order: the time up to LastEventAt is already counted
        if (at < record.LastEventAt)
        {
            return [];
        }

        IReadOnlyList<(Guid, DateTimeOffset, DateTimeOffset)> counted = [];
        var end = at < record.LastRealAt + RealReportCutOff ? at : record.LastRealAt + RealReportCutOff;
        if (record.Playing && end > record.LastEventAt)
        {
            var to = end - record.LastEventAt > MaxGap ? record.LastEventAt + MaxGap : end;
            counted = [(record.OwnerUserId, record.LastEventAt, to)];
        }

        record.Playing = !playbackEvent.IsPaused;
        record.LastEventAt = at;
        if (!playbackEvent.IsAutomated)
        {
            record.LastRealAt = at;
        }

        return counted;
    }

    /// <summary>
    /// Removes the playbacks with no event for <see cref="IdleTimeout"/>.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>How many were removed.</returns>
    public int Sweep(DateTimeOffset now)
    {
        var idle = _records.Where(r => now - r.Value.LastEventAt >= IdleTimeout).Select(r => r.Key).ToList();
        foreach (var key in idle)
        {
            _records.Remove(key);
        }

        return idle.Count;
    }

    private sealed class Record(Guid ownerUserId, DateTimeOffset lastEventAt, DateTimeOffset lastRealAt, bool playing)
    {
        public Guid OwnerUserId { get; } = ownerUserId;

        public DateTimeOffset LastEventAt { get; set; } = lastEventAt;

        public DateTimeOffset LastRealAt { get; set; } = lastRealAt;

        public bool Playing { get; set; } = playing;
    }
}
