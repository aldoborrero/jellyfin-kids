using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Predicts when a kid's allowed hours close, for the 5- and 1-minute notices only (research R8). Whether a kid is
/// allowed now is always Jellyfin's own <c>IsParentalScheduleAllowed</c>; this repeats its formula (server local time,
/// the schedule's days, inclusive start and end hours) because Jellyfin can only answer for the current instant.
/// </summary>
public static class ScheduleClock
{
    /// <summary>
    /// Whether the schedules allow a moment, by Jellyfin's rule: no schedules allow everything.
    /// </summary>
    /// <param name="schedules">The user's access schedules.</param>
    /// <param name="at">The moment.</param>
    /// <param name="zone">The server's time zone.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool IsAllowedAt(IReadOnlyCollection<AccessSchedule> schedules, DateTimeOffset at, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        if (schedules.Count == 0)
        {
            return true;
        }

        var local = TimeZoneInfo.ConvertTime(at, zone);
        var hour = local.TimeOfDay.TotalHours;
        return schedules.Any(s => s.DayOfWeek.Contains(local.DayOfWeek) && hour >= s.StartHour && hour <= s.EndHour);
    }

    /// <summary>
    /// When the allowed window closes, if it closes within the horizon, to the second.
    /// </summary>
    /// <param name="schedules">The user's access schedules.</param>
    /// <param name="now">The current time.</param>
    /// <param name="zone">The server's time zone.</param>
    /// <param name="horizon">How far ahead to look.</param>
    /// <returns>The first disallowed second, or null if allowed throughout (or not allowed now).</returns>
    public static DateTimeOffset? ClosesWithin(IReadOnlyCollection<AccessSchedule> schedules, DateTimeOffset now, TimeZoneInfo zone, TimeSpan horizon)
    {
        if (!IsAllowedAt(schedules, now, zone) || IsAllowedAt(schedules, now + horizon, zone))
        {
            return null;
        }

        // Allowed at lo, not at hi: narrow down to the first disallowed second
        var lo = now;
        var hi = now + horizon;
        while (hi - lo > TimeSpan.FromSeconds(1))
        {
            var mid = lo + ((hi - lo) / 2);
            if (IsAllowedAt(schedules, mid, zone))
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return hi;
    }
}
