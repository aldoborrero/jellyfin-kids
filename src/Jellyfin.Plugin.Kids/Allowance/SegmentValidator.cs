using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Kids.Storage;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// The rules for offline reports (research R8, FR-019). One invalid segment makes the whole report invalid.
/// </summary>
public static class SegmentValidator
{
    /// <summary>The most segments in one report.</summary>
    public const int MaxSegments = 100;

    /// <summary>The longest segment id.</summary>
    public const int MaxIdLength = 64;

    /// <summary>The longest segment, in seconds (12 hours).</summary>
    public const int MaxSeconds = 43_200;

    /// <summary>How far in the future a segment may start, for clock skew.</summary>
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Validates a report.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The errors keyed <c>Segments</c> or <c>Segments[i].Field</c>; empty when the report is valid.</returns>
    public static Dictionary<string, string[]> Validate(IReadOnlyList<SegmentInput>? segments, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (segments is null || segments.Count is 0 or > MaxSegments)
        {
            errors["Segments"] = [string.Create(CultureInfo.InvariantCulture, $"Send between 1 and {MaxSegments} segments.")];
            return errors;
        }

        var oldest = now.AddDays(-UsageStore.RetentionDays);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var key = string.Create(CultureInfo.InvariantCulture, $"Segments[{i}]");
            if (segment is null)
            {
                errors[key] = ["Missing segment."];
                continue;
            }

            if (string.IsNullOrWhiteSpace(segment.Id) || segment.Id.Length > MaxIdLength)
            {
                errors[key + ".Id"] = [string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxIdLength} characters.")];
            }
            else if (!ids.Add(segment.Id))
            {
                errors[key + ".Id"] = ["Appears more than once in this report."];
            }

            if (segment.Seconds is not (>= 1 and <= MaxSeconds))
            {
                errors[key + ".Seconds"] = [string.Create(CultureInfo.InvariantCulture, $"Must be between 1 and {MaxSeconds}.")];
            }

            if (segment.StartedAt is not { } started || started > now + MaxFutureSkew || started < oldest)
            {
                errors[key + ".StartedAt"] = [string.Create(CultureInfo.InvariantCulture, $"Must be within the last {UsageStore.RetentionDays} days and at most {MaxFutureSkew.TotalMinutes} minutes in the future.")];
            }
        }

        return errors;
    }
}
