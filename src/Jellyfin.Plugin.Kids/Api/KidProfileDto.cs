using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// A kid's profile as clients see it; also the body of a 409 answer.
/// </summary>
/// <param name="UserId">The Jellyfin user id.</param>
/// <param name="Revision">The current revision.</param>
/// <param name="SearchAllowed">Whether the kid may search.</param>
/// <param name="AutoplayEnabled">Whether autoplay is on.</param>
/// <param name="AutoplayCap">How many episodes autoplay may play in a row.</param>
/// <param name="Sections">The home sections in order, with their visibility.</param>
/// <param name="PickedItemIds">The picked collections and playlists in order.</param>
/// <param name="DailyAllowanceMinutes">The daily screen-time allowance in minutes, 0–1440; absent when unlimited.</param>
/// <param name="SessionLimitMinutes">The sitting limit in minutes, 5–120, counted by each device; absent when there is none.</param>
/// <param name="LiveTvChannelIds">The live TV channels the kid may watch, in order; empty when they have no live TV.</param>
/// <param name="Avatar">The kid's avatar id (<c>leo</c>, <c>max</c>, <c>nico</c>, <c>lucy</c>, <c>sara</c> or <c>mia</c>); absent when the client should show the Jellyfin profile image.</param>
public record KidProfileDto(
    Guid UserId,
    long Revision,
    bool SearchAllowed,
    bool AutoplayEnabled,
    int AutoplayCap,
    IReadOnlyList<HomeSectionEntry> Sections,
    IReadOnlyList<Guid> PickedItemIds,
    int? DailyAllowanceMinutes,
    int? SessionLimitMinutes,
    IReadOnlyList<Guid> LiveTvChannelIds,
    string? Avatar)
{
    /// <summary>
    /// Maps a stored profile.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The DTO.</returns>
    public static KidProfileDto From(KidProfile profile) => new(
        profile.UserId,
        profile.Revision,
        profile.Settings.SearchAllowed,
        profile.Settings.AutoplayEnabled,
        profile.Settings.AutoplayCap,
        profile.Settings.Sections,
        profile.Settings.PickedItemIds,
        profile.Settings.DailyAllowanceMinutes,
        profile.Settings.SessionLimitMinutes,
        profile.Settings.LiveTvChannelIds,
        profile.Settings.Avatar);
}
