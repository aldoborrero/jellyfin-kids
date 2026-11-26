namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// The time one series (or movie, or channel) took in a day (feature 22).
/// </summary>
/// <param name="Key">The grouping key, the same as <see cref="SeriesDetail.Key"/>.</param>
/// <param name="Name">The name.</param>
/// <param name="IsLiveTv">Whether it is live TV.</param>
/// <param name="Seconds">The seconds.</param>
public sealed record DaySeries(string Key, string Name, bool IsLiveTv, int Seconds);
