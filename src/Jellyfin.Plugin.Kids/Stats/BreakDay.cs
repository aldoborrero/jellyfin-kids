namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// A day's breaks after the sitting limit (feature 22).
/// </summary>
/// <param name="Day">The household day, <c>yyyy-MM-dd</c>.</param>
/// <param name="Count">How many breaks started that day.</param>
/// <param name="Minutes">How long they lasted together.</param>
public sealed record BreakDay(string Day, int Count, int Minutes);
