namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// A kid's day as stored (feature 22): used time, bonus and the allowance noted for it.
/// </summary>
/// <param name="UsedMs">The milliseconds used.</param>
/// <param name="BonusMinutes">The bonus minutes.</param>
/// <param name="AllowanceKnown">Whether an allowance was noted for the day.</param>
/// <param name="AllowanceMinutes">The noted allowance, null for no daily limit.</param>
public sealed record StoredDay(long UsedMs, int BonusMinutes, bool AllowanceKnown, int? AllowanceMinutes);
