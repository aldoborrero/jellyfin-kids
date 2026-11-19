using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// A kid's statistics for clients, the answer of <c>GET /Kids/Users/{userId}/Stats</c> (feature 22, contract
/// stats-contract.md). Today's live figures stay on the Status.
/// </summary>
/// <param name="UserId">The kid.</param>
/// <param name="Today">The current household day, <c>yyyy-MM-dd</c>.</param>
/// <param name="AdvancedStats">Whether the household keeps the watch log; without it TopSeries and Breaks are empty.</param>
/// <param name="RetentionDays">How many days are kept.</param>
/// <param name="Days">One entry per day of the range, oldest first, today last, days with nothing included.</param>
/// <param name="TopSeries">At most 5, by minutes over the range.</param>
/// <param name="Breaks">The days with breaks, oldest first.</param>
public sealed record KidStats(
    Guid UserId,
    string Today,
    bool AdvancedStats,
    int RetentionDays,
    IReadOnlyList<DayStats> Days,
    IReadOnlyList<SeriesStats> TopSeries,
    IReadOnlyList<BreakDay> Breaks);
