using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// A series, movie or channel on the dashboard (feature 22), with its episodes.
/// </summary>
/// <param name="Key">The grouping key: <c>series:</c>, <c>item:</c> or <c>live:</c> and an id, or <c>name:</c> and a name.</param>
/// <param name="SeriesId">The series, item or channel id; null when unknown.</param>
/// <param name="Name">The name.</param>
/// <param name="IsLiveTv">Whether it is live TV.</param>
/// <param name="Seconds">The seconds over the range.</param>
/// <param name="Plays">How many playbacks.</param>
/// <param name="LastWatched">When it was last watched.</param>
/// <param name="Episodes">The episodes of a series, most watched first; empty for other entries.</param>
public sealed record SeriesDetail(
    string Key,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? SeriesId,
    string Name,
    bool IsLiveTv,
    int Seconds,
    int Plays,
    DateTime LastWatched,
    IReadOnlyList<EpisodeStats> Episodes);
