using System;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// A break after the sitting limit, as the watch log keeps it (feature 22).
/// </summary>
/// <param name="UserId">The kid.</param>
/// <param name="Day">The household day it started in, <c>yyyy-MM-dd</c>.</param>
/// <param name="StartedAt">When the sitting limit was reached; identifies the break.</param>
/// <param name="Minutes">How long it lasted: the household break, or less when a grown-up ended it.</param>
/// <param name="EndedByGrownUp">Whether a grown-up ended it early (End break or Reset).</param>
public sealed record BreakRecord(Guid UserId, string Day, DateTimeOffset StartedAt, int Minutes, bool EndedByGrownUp);
