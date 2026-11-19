using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// One day on the dashboard (feature 22): screen time from the daily usage, and what was watched from the watch log.
/// </summary>
/// <param name="Day">The household day, <c>yyyy-MM-dd</c>.</param>
/// <param name="UsedSeconds">The seconds used, from the daily usage (every client and offline reports).</param>
/// <param name="BonusMinutes">The bonus minutes.</param>
/// <param name="AllowanceMinutes">The day's allowance; null is no daily limit and is always written.</param>
/// <param name="Series">What the watch log has for the day, by series, most first; empty without it.</param>
public sealed record DetailDay(
    string Day,
    int UsedSeconds,
    int BonusMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? AllowanceMinutes,
    IReadOnlyList<DaySeries> Series);
