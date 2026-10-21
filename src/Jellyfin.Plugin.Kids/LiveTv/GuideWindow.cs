using System;

namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// The time window of a kid's guide request (contract: default now to +24 h, at most 7 days). Pure.
/// </summary>
/// <param name="Start">Programs must end after this, in UTC.</param>
/// <param name="End">Programs must start before this, in UTC.</param>
public sealed record GuideWindow(DateTime Start, DateTime End)
{
    /// <summary>The window when the request names no end.</summary>
    public static readonly TimeSpan DefaultLength = TimeSpan.FromHours(24);

    /// <summary>The longest window; longer ones are cut.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromDays(7);

    /// <summary>
    /// Resolves a request's window.
    /// </summary>
    /// <param name="start">The requested start, or null for now.</param>
    /// <param name="end">The requested end, or null for the start plus 24 hours.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The window, or null when the end is not after the start.</returns>
    public static GuideWindow? Resolve(DateTime? start, DateTime? end, DateTimeOffset now)
    {
        var from = start is { } s ? Utc(s) : now.UtcDateTime;
        var to = end is { } e ? Utc(e) : from > DateTime.MaxValue - DefaultLength ? DateTime.MaxValue : from + DefaultLength;
        if (to <= from)
        {
            return null;
        }

        return new GuideWindow(from, to - from > MaxLength ? from + MaxLength : to);
    }

    // Query values without an offset are read as UTC, as the contract says
    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
