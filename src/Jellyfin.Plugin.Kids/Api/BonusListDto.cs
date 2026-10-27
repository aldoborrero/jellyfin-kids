using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Kids.Allowance;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// A kid's bonuses of the current day (feature 13, <c>GET /Kids/Users/{userId}/WatchTime/Bonus</c>).
/// </summary>
/// <param name="Date">Today, <c>yyyy-MM-dd</c>, under the household's "day starts at" rule.</param>
/// <param name="ServerTime">The server's current time (UTC); what <c>CanUndo</c> was judged on.</param>
/// <param name="UndoWindowSeconds">How long after it was given a bonus can be undone.</param>
/// <param name="Bonuses">The bonuses, newest first.</param>
public record BonusListDto(string Date, DateTime ServerTime, int UndoWindowSeconds, IReadOnlyList<BonusDto> Bonuses)
{
    /// <summary>
    /// Maps today's bonuses.
    /// </summary>
    /// <param name="today">The bonuses.</param>
    /// <returns>The DTO.</returns>
    public static BonusListDto From(TodayBonuses today)
    {
        ArgumentNullException.ThrowIfNull(today);
        return new BonusListDto(
            DayClock.Format(today.Date),
            today.Now.UtcDateTime,
            (int)BonusUndo.Window.TotalSeconds,
            today.Bonuses.Select(BonusDto.From).ToList());
    }
}
