namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// What a parent's "Reset today" cleared (feature 7), for the log and the answer.
/// </summary>
/// <param name="Day">The kid's day that was reset, <c>yyyy-MM-dd</c>.</param>
/// <param name="UsedMs">The playing time cleared, stored and not yet stored.</param>
/// <param name="BonusMinutes">The bonus minutes cleared.</param>
/// <param name="Status">The kid's status after the reset.</param>
public sealed record DayReset(string Day, long UsedMs, int BonusMinutes, KidStatus Status);
