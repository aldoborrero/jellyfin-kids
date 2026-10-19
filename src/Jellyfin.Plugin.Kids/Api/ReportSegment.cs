using System;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// One offline playback segment.
/// </summary>
public class ReportSegment
{
    /// <summary>
    /// Gets the client's id for the segment, 1–64 characters, unique per kid; a repeated id is ignored.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Gets when the playback started: within the last 90 days and at most 5 minutes in the future.
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// Gets how long it played, 1–43 200 seconds.
    /// </summary>
    public int? Seconds { get; init; }
}
