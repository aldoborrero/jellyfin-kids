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

    /// <summary>The lowest daily allowance, in minutes.</summary>
    public const int MinDailyAllowance = 0;

    /// <summary>The highest daily allowance, in minutes (a whole day).</summary>
    public const int MaxDailyAllowance = 1440;

    /// <summary>The shortest session limit, in minutes.</summary>
    public const int MinSessionLimit = 5;

    /// <summary>The longest session limit, in minutes.</summary>
    public const int MaxSessionLimit = 120;

    /// <summary>The most live TV channels a kid can be allowed.</summary>
    public const int MaxLiveTvChannels = 200;

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

    /// <summary>
    /// Gets the daily screen-time allowance in minutes, 0–1440 inclusive; null means unlimited and is the default.
    /// </summary>
    public int? DailyAllowanceMinutes { get; init; }

    /// <summary>
    /// Gets the longest a single sitting may last, in minutes, 5–120 inclusive; null means no sitting limit and is the
    /// default. The server only stores it: each device counts its own sittings.
    /// </summary>
    public int? SessionLimitMinutes { get; init; }

    /// <summary>
    /// Gets the live TV channels the kid may watch, in order. Empty, the default, means no live TV.
    /// </summary>
    public IReadOnlyList<Guid> LiveTvChannelIds { get; init; } = [];

    /// <summary>
    /// Gets the kid's avatar, one of <see cref="KidAvatars.Ids"/>; null, the default, means the Jellyfin profile image.
    /// </summary>
    public string? Avatar { get; init; }
}
