using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// The kids mode settings of a kid profile. Defaults mirror Wholphin's local <c>KidProfile</c>.
/// </summary>
public sealed record KidProfileSettings
{
    /// <summary>The lowest autoplay limit.</summary>
    public const int MinAutoplayCap = 1;

    /// <summary>The highest autoplay limit.</summary>
    public const int MaxAutoplayCap = 10;

    /// <summary>The default autoplay limit.</summary>
    public const int DefaultAutoplayCap = 3;

    /// <summary>The most picked items a kid can have.</summary>
    public const int MaxPicks = 50;

    /// <summary>
    /// Gets a value indicating whether the kid may search.
    /// </summary>
    public bool SearchAllowed { get; init; }

    /// <summary>
    /// Gets a value indicating whether the next episode plays automatically.
    /// </summary>
    public bool AutoplayEnabled { get; init; }

    /// <summary>
    /// Gets how many episodes autoplay may play in a row, 1–10 inclusive.
    /// </summary>
    public int AutoplayCap { get; init; } = DefaultAutoplayCap;

    /// <summary>
    /// Gets the home sections in order, each with its visibility.
    /// </summary>
    public IReadOnlyList<HomeSectionEntry> Sections { get; init; } = HomeSectionEntry.Default;

    /// <summary>
    /// Gets the collections and playlists picked by the grown-ups, in order.
    /// </summary>
    public IReadOnlyList<Guid> PickedItemIds { get; init; } = [];
}
