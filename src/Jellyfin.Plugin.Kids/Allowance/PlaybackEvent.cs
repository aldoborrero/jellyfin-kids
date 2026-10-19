using System;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// What kind of playback report an event is.
/// </summary>
public enum PlaybackEventKind
{
    /// <summary>Playback started.</summary>
    Start,

    /// <summary>A progress report, real or synthesised by the server.</summary>
    Progress,

    /// <summary>Playback stopped.</summary>
    Stop
}

/// <summary>
/// The fields of a Jellyfin playback event that the counting rule needs.
/// </summary>
/// <param name="Kind">Start, progress or stop.</param>
/// <param name="SessionId">The Jellyfin session.</param>
/// <param name="PlaySessionId">The playback within the session, if the client sent one.</param>
/// <param name="OwnerUserId">The user who owns the session.</param>
/// <param name="IsPaused">Whether the playback is paused.</param>
/// <param name="IsAutomated">Whether the server synthesised the report (only progress reports can be).</param>
/// <param name="At">When the event was received.</param>
public sealed record PlaybackEvent(
    PlaybackEventKind Kind,
    string SessionId,
    string? PlaySessionId,
    Guid OwnerUserId,
    bool IsPaused,
    bool IsAutomated,
    DateTimeOffset At);
