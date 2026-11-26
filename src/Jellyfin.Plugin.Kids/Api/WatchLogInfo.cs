namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The watch log's state, for the dashboard's statistics settings (feature 22).
/// </summary>
/// <param name="AdvancedStats">Whether it is recorded now.</param>
/// <param name="RetentionDays">How many days are kept.</param>
/// <param name="Segments">How many watch segments are stored, all kids together.</param>
/// <param name="Breaks">How many breaks are stored.</param>
/// <param name="OldestDay">The oldest day stored, <c>yyyy-MM-dd</c>, or null when it is empty.</param>
public sealed record WatchLogInfo(bool AdvancedStats, int RetentionDays, long Segments, long Breaks, string? OldestDay);
