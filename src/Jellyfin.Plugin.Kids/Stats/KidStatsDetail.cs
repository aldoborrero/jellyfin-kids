using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// A kid's statistics for the dashboard's statistics page (feature 22): the plugin's own shape, not part of the client
/// contract. Times are in seconds.
/// </summary>
/// <param name="UserId">The kid.</param>
/// <param name="Today">The current household day, <c>yyyy-MM-dd</c>.</param>
/// <param name="AdvancedStats">Whether the watch log is recorded now.</param>
/// <param name="RetentionDays">How many days are kept.</param>
/// <param name="Days">One entry per day of the range, oldest first.</param>
/// <param name="Series">Everything watched, by time, with episodes.</param>
/// <param name="Hours">When the kid watches.</param>
/// <param name="Devices">Where the kid watches.</param>
/// <param name="Breaks">The days with breaks.</param>
public sealed record KidStatsDetail(
    Guid UserId,
    string Today,
    bool AdvancedStats,
    int RetentionDays,
    IReadOnlyList<DetailDay> Days,
    IReadOnlyList<SeriesDetail> Series,
    HourGrid Hours,
    IReadOnlyList<DeviceStats> Devices,
    IReadOnlyList<BreakDay> Breaks);
