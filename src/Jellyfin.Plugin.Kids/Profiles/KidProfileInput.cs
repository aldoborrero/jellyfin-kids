using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// Kid profile settings as sent by a client, before validation. Omitted values take their defaults.
/// </summary>
public sealed record KidProfileInput
{
    /// <summary>
    /// Gets whether the kid may search.
    /// </summary>
    public bool? SearchAllowed { get; init; }

    /// <summary>
    /// Gets whether autoplay is on.
    /// </summary>
    public bool? AutoplayEnabled { get; init; }

    /// <summary>
    /// Gets the autoplay limit.
    /// </summary>
    public int? AutoplayCap { get; init; }

    /// <summary>
    /// Gets the home sections in order. Section names are strings so that unknown names reach validation.
    /// </summary>
    public IReadOnlyList<SectionInput>? Sections { get; init; }

    /// <summary>
    /// Gets the picked collections and playlists in order.
    /// </summary>
    public IReadOnlyList<Guid>? PickedItemIds { get; init; }

    /// <summary>
    /// Gets the daily allowance in minutes; null means unlimited.
    /// </summary>
    public int? DailyAllowanceMinutes { get; init; }

    /// <summary>
    /// Gets the session limit in minutes; null means no sitting limit.
    /// </summary>
    public int? SessionLimitMinutes { get; init; }

    /// <summary>
    /// Gets the live TV channels the kid may watch, in order; null or empty means no live TV.
    /// </summary>
    public IReadOnlyList<Guid>? LiveTvChannelIds { get; init; }
}
