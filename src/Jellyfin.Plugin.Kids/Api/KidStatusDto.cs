using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// How a kid is doing today, as clients see it. Null values are left out of the JSON.
/// </summary>
/// <param name="Date">Today, <c>yyyy-MM-dd</c>, under the household's "day starts at" rule.</param>
/// <param name="UsedSeconds">Time used today, including time counted but not yet stored.</param>
/// <param name="AllowanceSeconds">The daily allowance; absent when unlimited.</param>
/// <param name="BonusSeconds">The bonus added today.</param>
/// <param name="RemainingSeconds">Allowance plus bonus minus used, never negative; absent when unlimited.</param>
/// <param name="TimeUp">Whether an allowance is set and used has reached allowance plus bonus.</param>
/// <param name="ServerTime">The server's current time (UTC).</param>
/// <param name="NextDayStartsAt">When the next day starts (UTC).</param>
/// <param name="WarnAtSeconds">The remaining times at which clients should warn.</param>
/// <param name="Enforced">Whether the household turned server enforcement on (feature 5).</param>
/// <param name="DeniedReason">While enforced and denied: <c>timeUp</c>, <c>outsideSchedule</c> or <c>sessionLimit</c>.</param>
/// <param name="DeniedUntil">When the denial ends if nothing changes (UTC); absent for <c>outsideSchedule</c>.</param>
/// <param name="SittingLimitSeconds">The kid's sitting limit; absent without one.</param>
/// <param name="SittingUsedSeconds">Playing time in the current sitting, across devices; with the limit.</param>
/// <param name="SittingRemainingSeconds">Sitting limit minus used, never negative; with the limit.</param>
/// <param name="BreakRemainingSeconds">Seconds until the kid may play again, during the break after the limit.</param>
/// <param name="BreakSeconds">The household break; with the limit.</param>
/// <param name="BonusId">Only in the answer to <c>POST …/WatchTime/Bonus</c>: the bonus just given (feature 13).</param>
/// <param name="BonusUndoUntil">Only in that answer: until when that bonus can be undone (UTC).</param>
public record KidStatusDto(
    string Date,
    long UsedSeconds,
    long? AllowanceSeconds,
    long BonusSeconds,
    long? RemainingSeconds,
    bool TimeUp,
    DateTime ServerTime,
    DateTime NextDayStartsAt,
    IReadOnlyList<int> WarnAtSeconds,
    bool Enforced = false,
    string? DeniedReason = null,
    DateTime? DeniedUntil = null,
    long? SittingLimitSeconds = null,
    long? SittingUsedSeconds = null,
    long? SittingRemainingSeconds = null,
    long? BreakRemainingSeconds = null,
    long? BreakSeconds = null,
    long? BonusId = null,
    DateTime? BonusUndoUntil = null)
{
    /// <summary>
    /// Maps a status, with the kid's enforcement state when there is one.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="enforcement">The enforcement state.</param>
    /// <returns>The DTO.</returns>
    public static KidStatusDto From(KidStatus status, EnforcementView? enforcement = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        var dto = new KidStatusDto(
            DayClock.Format(status.Date),
            status.UsedSeconds,
            status.AllowanceSeconds,
            status.BonusSeconds,
            status.RemainingSeconds,
            status.TimeUp,
            status.ServerTime.UtcDateTime,
            status.NextDayStartsAt.UtcDateTime,
            status.WarnAtSeconds);
        if (enforcement is null)
        {
            return dto;
        }

        dto = dto with
        {
            Enforced = enforcement.Enforced,
            DeniedReason = enforcement.Denial?.Code,
            DeniedUntil = enforcement.Denial?.Until?.UtcDateTime,
        };
        if (enforcement.SittingLimit is not { } limit || enforcement.Sitting is not { } sitting)
        {
            return dto;
        }

        return dto with
        {
            SittingLimitSeconds = (long)limit.TotalSeconds,
            SittingUsedSeconds = sitting.UsedMs / 1000,
            SittingRemainingSeconds = sitting.RemainingMs / 1000,
            BreakRemainingSeconds = sitting.BreakEndsAt is { } ends
                ? Math.Max(0, (long)Math.Ceiling((ends - status.ServerTime).TotalSeconds))
                : null,
            BreakSeconds = (long)enforcement.Break.TotalSeconds,
        };
    }
}
