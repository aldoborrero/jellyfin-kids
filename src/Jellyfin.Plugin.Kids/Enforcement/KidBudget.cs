using System;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// A kid's allowance for one day, kept in memory so playback events and requests are decided without I/O
/// (data-model.md "KidBudget", research R6).
/// </summary>
/// <remarks>Not thread-safe: <see cref="EnforcementState"/> serialises calls.</remarks>
public sealed class KidBudget
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KidBudget"/> class.
    /// </summary>
    /// <param name="day">The day it describes.</param>
    /// <param name="usedMs">Time used that day, stored and pending.</param>
    /// <param name="allowanceMinutes">The daily allowance, or null when unlimited.</param>
    /// <param name="bonusMinutes">The bonus added that day.</param>
    public KidBudget(DateOnly day, long usedMs, int? allowanceMinutes, int bonusMinutes)
    {
        Day = day;
        UsedMs = Math.Max(0, usedMs);
        AllowanceMinutes = allowanceMinutes;
        BonusMinutes = bonusMinutes;
    }

    /// <summary>Gets the day this budget describes.</summary>
    public DateOnly Day { get; private set; }

    /// <summary>Gets the time used that day.</summary>
    public long UsedMs { get; private set; }

    /// <summary>Gets the daily allowance, or null when unlimited.</summary>
    public int? AllowanceMinutes { get; }

    /// <summary>Gets the bonus added that day.</summary>
    public int BonusMinutes { get; private set; }

    /// <summary>Gets the allowance plus bonus, or null when unlimited.</summary>
    public long? LimitMs => AllowanceMinutes is int allowance ? (allowance + (long)BonusMinutes) * 60_000 : null;

    /// <summary>Gets a value indicating whether an allowance is set and used has reached it.</summary>
    public bool TimeUp => LimitMs is long limit && UsedMs >= limit;

    /// <summary>Gets the time left, never negative, or null when unlimited.</summary>
    public long? RemainingMs => LimitMs is long limit ? Math.Max(0, limit - UsedMs) : null;

    /// <summary>
    /// Adds playing time to a day: the budget's own day, or a later one, which starts the budget afresh.
    /// </summary>
    /// <param name="day">The day the time belongs to.</param>
    /// <param name="ms">The time.</param>
    /// <returns><c>true</c> if the budget moved to a new day and should be reloaded.</returns>
    public bool Add(DateOnly day, long ms)
    {
        var rolled = RollTo(day);
        if (day == Day && ms > 0)
        {
            UsedMs += ms;
        }

        return rolled;
    }

    /// <summary>
    /// Moves the budget to a later day: nothing used, the same allowance, no bonus yet, which is exactly a new day.
    /// </summary>
    /// <param name="day">The day it is now.</param>
    /// <returns><c>true</c> if it moved.</returns>
    public bool RollTo(DateOnly day)
    {
        if (day <= Day)
        {
            return false;
        }

        Day = day;
        UsedMs = 0;
        BonusMinutes = 0;
        return true;
    }
}
