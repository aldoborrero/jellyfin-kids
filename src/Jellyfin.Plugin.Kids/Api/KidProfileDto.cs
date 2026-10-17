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
public record KidProfileDto(
    Guid UserId,
    long Revision,
    bool SearchAllowed,
    bool AutoplayEnabled,
    int AutoplayCap,
    IReadOnlyList<HomeSectionEntry> Sections,
    IReadOnlyList<Guid> PickedItemIds)
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
        profile.Settings.PickedItemIds);
}
