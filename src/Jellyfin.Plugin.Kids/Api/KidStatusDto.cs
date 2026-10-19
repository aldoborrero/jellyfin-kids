using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.Allowance;

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
public record KidStatusDto(
    string Date,
    long UsedSeconds,
    long? AllowanceSeconds,
    long BonusSeconds,
    long? RemainingSeconds,
    bool TimeUp,
    DateTime ServerTime,
    DateTime NextDayStartsAt,
    IReadOnlyList<int> WarnAtSeconds)
{
    /// <summary>
    /// Maps a status.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The DTO.</returns>
    public static KidStatusDto From(KidStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new KidStatusDto(
            DayClock.Format(status.Date),
            status.UsedSeconds,
            status.AllowanceSeconds,
            status.BonusSeconds,
            status.RemainingSeconds,
            status.TimeUp,
            status.ServerTime.UtcDateTime,
            status.NextDayStartsAt.UtcDateTime,
            status.WarnAtSeconds);
    }
}
