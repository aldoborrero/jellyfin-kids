using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The body of <c>PUT /Kids/Users/{userId}/Profile</c>: a full replacement, where omitted settings take their defaults.
/// </summary>
public class KidProfileUpdate
{
    /// <summary>
    /// Gets the revision the change is based on: 0 marks the user as a kid. Nullable so that a missing value is a
    /// validation error instead of meaning "create".
    /// </summary>
    [Required]
    public long? Revision { get; init; }

    /// <summary>
    /// Gets whether the kid may search (default false).
    /// </summary>
    public bool? SearchAllowed { get; init; }

    /// <summary>
    /// Gets whether autoplay is on (default false).
    /// </summary>
    public bool? AutoplayEnabled { get; init; }

    /// <summary>
    /// Gets the autoplay limit, 1–10 (default 3).
    /// </summary>
    public int? AutoplayCap { get; init; }

    /// <summary>
    /// Gets the home sections in order; missing ones are appended, visible.
    /// </summary>
    public IReadOnlyList<KidSectionUpdate>? Sections { get; init; }

    /// <summary>
    /// Gets the picked collections and playlists in order.
    /// </summary>
    public IReadOnlyList<Guid>? PickedItemIds { get; init; }

    /// <summary>
    /// Gets the daily screen-time allowance in minutes, 0–1440; null or omitted means unlimited. The update is a full
    /// replacement, so omitting it clears the allowance: send it on every update.
    /// </summary>
    public int? DailyAllowanceMinutes { get; init; }

    /// <summary>
    /// Gets the longest a single sitting may last in minutes, 5–120; null or omitted means no sitting limit. The server
    /// only stores it, and each device counts its own sittings. Like the allowance, omitting it clears it: send it on
    /// every update.
    /// </summary>
    public int? SessionLimitMinutes { get; init; }

    /// <summary>
    /// Gets the live TV channels the kid may watch, in order (at most 200); null, omitted or empty means no live TV.
    /// Like the limits, omitting it clears it: send it on every update.
    /// </summary>
    public IReadOnlyList<Guid>? LiveTvChannelIds { get; init; }

    /// <summary>
    /// Gets the avatar id: <c>leo</c>, <c>max</c>, <c>nico</c>, <c>lucy</c>, <c>sara</c> or <c>mia</c>, in any case;
    /// null, omitted or blank means none (the Jellyfin profile image). Like the limits, omitting it clears it: send it
    /// on every update.
    /// </summary>
    public string? Avatar { get; init; }

    /// <summary>
    /// Maps the body to the validator's input.
    /// </summary>
    /// <returns>The input.</returns>
    public KidProfileInput ToInput() => new()
    {
        SearchAllowed = SearchAllowed,
        AutoplayEnabled = AutoplayEnabled,
        AutoplayCap = AutoplayCap,
        Sections = Sections?.Select(s => new SectionInput(s.Section, s.Visible)).ToList(),
        PickedItemIds = PickedItemIds,
        DailyAllowanceMinutes = DailyAllowanceMinutes,
        SessionLimitMinutes = SessionLimitMinutes,
        LiveTvChannelIds = LiveTvChannelIds,
        Avatar = Avatar
    };
}
