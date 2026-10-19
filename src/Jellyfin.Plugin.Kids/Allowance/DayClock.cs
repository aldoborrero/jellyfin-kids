using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// The household's day: from H:00 local time to the next H:00 (research R4). Daylight-saving aware: when H:00 does not
/// exist (spring forward) the day starts at the first valid local time after it, and when it happens twice (fall back)
/// at the earlier one, so such days are 23 or 25 hours long.
/// </summary>
public sealed class DayClock
{
    private readonly TimeZoneInfo _zone;
    private readonly int _startHour;

    /// <summary>
    /// Initializes a new instance of the <see cref="DayClock"/> class.
    /// </summary>
    /// <param name="zone">The time zone days are counted in.</param>
    /// <param name="startHour">The local hour a day starts at, 0–23.</param>
    public DayClock(TimeZoneInfo zone, int startHour)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfNegative(startHour);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startHour, 23);
        _zone = zone;
        _startHour = startHour;
    }

    /// <summary>
    /// Writes a day as stored and sent to clients.
    /// </summary>
    /// <param name="day">The day.</param>
    /// <returns>The day as <c>yyyy-MM-dd</c>.</returns>
    public static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The day an instant belongs to.
    /// </summary>
    /// <param name="utc">The instant.</param>
    /// <returns>The day.</returns>
    public DateOnly DayOf(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, _zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        return local.Hour < _startHour ? date.AddDays(-1) : date;
    }

    /// <summary>
    /// The instant a day starts.
    /// </summary>
    /// <param name="day">The day.</param>
    /// <returns>The start, in UTC.</returns>
    public DateTimeOffset StartOf(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(_startHour, 0), DateTimeKind.Unspecified);
        for (var i = 0; i < 24 * 60 && _zone.IsInvalidTime(local); i++)
        {
            local = local.AddMinutes(1);
        }

        var offset = _zone.IsAmbiguousTime(local)
            ? _zone.GetAmbiguousTimeOffsets(local).Max() // the larger offset is the earlier instant
            : _zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>
    /// When the day after the one an instant belongs to starts.
    /// </summary>
    /// <param name="utc">The instant.</param>
    /// <returns>The next start, in UTC; always after <paramref name="utc"/>.</returns>
    public DateTimeOffset NextStart(DateTimeOffset utc) => StartOf(DayOf(utc).AddDays(1));

    /// <summary>
    /// Splits an interval at day starts.
    /// </summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    /// <returns>Each day the interval touches with the time spent in it; nothing for an empty or reversed interval.</returns>
    public IEnumerable<(DateOnly Day, TimeSpan Length)> Split(DateTimeOffset from, DateTimeOffset to)
    {
        while (from < to)
        {
            var next = NextStart(from);
            var end = next < to ? next : to;
            yield return (DayOf(from), end - from);
            from = end;
        }
    }
}
