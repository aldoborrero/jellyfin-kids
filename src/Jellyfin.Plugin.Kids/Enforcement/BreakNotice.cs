using System;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// A break after the sitting limit started, or a grown-up ended one early (feature 22, for the watch log).
/// </summary>
/// <param name="Kid">The kid.</param>
/// <param name="StartedAt">When the sitting limit was reached; identifies the break.</param>
/// <param name="Length">The household break when it starts; the time it lasted when a grown-up ends it.</param>
/// <param name="EndedByGrownUp">Whether a grown-up ended it (End break or Reset).</param>
public sealed record BreakNotice(Guid Kid, DateTimeOffset StartedAt, TimeSpan Length, bool EndedByGrownUp);
