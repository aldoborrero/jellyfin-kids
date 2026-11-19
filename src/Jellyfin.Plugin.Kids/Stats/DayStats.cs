using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// One day of a kid's screen time (feature 22).
/// </summary>
/// <param name="Day">The household day, <c>yyyy-MM-dd</c>.</param>
/// <param name="UsedMinutes">The whole minutes used, as the Status counts them.</param>
/// <param name="BonusMinutes">The bonus minutes given that day.</param>
/// <param name="AllowanceMinutes">
/// The day's allowance as the profile had it, or today's when the day has none noted; null is no daily limit and is
/// always written, never left out.
/// </param>
public sealed record DayStats(
    string Day,
    int UsedMinutes,
    int BonusMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? AllowanceMinutes);
