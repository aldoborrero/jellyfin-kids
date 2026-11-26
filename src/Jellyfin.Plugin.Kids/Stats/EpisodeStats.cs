using System;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// One episode on the dashboard (feature 22).
/// </summary>
/// <param name="ItemId">The episode.</param>
/// <param name="Name">The episode title.</param>
/// <param name="Season">The season number.</param>
/// <param name="Episode">The episode number.</param>
/// <param name="Seconds">The seconds over the range.</param>
/// <param name="Plays">How many playbacks.</param>
/// <param name="LastWatched">When it was last watched.</param>
public sealed record EpisodeStats(Guid? ItemId, string Name, int? Season, int? Episode, int Seconds, int Plays, DateTime LastWatched);
