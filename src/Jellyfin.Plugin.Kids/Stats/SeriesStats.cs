using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// One of a kid's most watched entries (feature 22): a series, a movie or other item on its own, or a live TV channel.
/// </summary>
/// <param name="SeriesId">The series, the item for a movie, or the channel for live TV; null when unknown, always written.</param>
/// <param name="Name">Its name.</param>
/// <param name="Minutes">The minutes watched over the range, rounded.</param>
/// <param name="Plays">How many playbacks.</param>
/// <param name="IsLiveTv">Whether it is a live TV channel.</param>
public sealed record SeriesStats(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? SeriesId,
    string Name,
    int Minutes,
    int Plays,
    bool IsLiveTv);
