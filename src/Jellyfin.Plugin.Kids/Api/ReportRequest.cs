using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Kids.Allowance;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The body of <c>POST /Kids/Users/{userId}/WatchTime/Report</c>: playback the server did not see.
/// </summary>
public class ReportRequest
{
    /// <summary>
    /// Gets the segments, 1–100.
    /// </summary>
    public IReadOnlyList<ReportSegment>? Segments { get; init; }

    /// <summary>
    /// Maps the body to the validator's input.
    /// </summary>
    /// <returns>The segments, or null if none were sent.</returns>
    public IReadOnlyList<SegmentInput>? ToInput()
        => Segments?.Select(s => s is null ? null! : new SegmentInput(s.Id, ToUtc(s.StartedAt), s.Seconds)).ToList();

    // A time without an offset is taken as UTC, like the rest of Jellyfin's API
    private static DateTimeOffset? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Unspecified } v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)),
        { } v => new DateTimeOffset(v.ToUniversalTime())
    };
}
