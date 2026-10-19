using System;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// The status arithmetic (research R6, FR-014).
/// </summary>
public static class StatusCalculator
{
    /// <summary>
    /// The remaining times, in seconds, at which clients should warn.
    /// </summary>
    public static readonly int[] WarnAtSeconds = [300, 60];

    /// <summary>
    /// Computes a kid's status.
    /// </summary>
    /// <param name="day">Today.</param>
    /// <param name="usedMs">Time used today, stored plus not yet stored.</param>
    /// <param name="allowanceMinutes">The daily allowance, or null when unlimited.</param>
    /// <param name="bonusMinutes">The bonus added today.</param>
    /// <param name="now">The current time.</param>
    /// <param name="nextDayStartsAt">When the next day starts.</param>
    /// <returns>The status.</returns>
    public static KidStatus Calculate(DateOnly day, long usedMs, int? allowanceMinutes, int bonusMinutes, DateTimeOffset now, DateTimeOffset nextDayStartsAt)
    {
        var used = Math.Max(0, usedMs) / 1000;
        var bonus = bonusMinutes * 60L;
        long? allowance = allowanceMinutes * 60L;
        long? remaining = allowance is null ? null : Math.Max(0, allowance.Value + bonus - used);
        var timeUp = allowance is not null && used >= allowance.Value + bonus;
        return new KidStatus(day, used, allowance, bonus, remaining, timeUp, now, nextDayStartsAt, WarnAtSeconds);
    }
}
