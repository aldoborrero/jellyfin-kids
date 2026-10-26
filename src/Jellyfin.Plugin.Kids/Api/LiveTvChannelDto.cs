using System;
using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// A live TV channel the dashboard editor can allow for a kid.
/// </summary>
/// <param name="Id">The channel item id.</param>
/// <param name="Name">The channel name.</param>
/// <param name="Number">The channel number; absent without one.</param>
/// <param name="ImageTag">The logo's image tag, for <c>Items/{Id}/Images/Primary</c>; absent without a logo.</param>
/// <param name="Type"><c>TV</c> or <c>Radio</c>.</param>
/// <param name="Rating">The rating Jellyfin's parental control compares; absent when the channel is unrated.</param>
/// <param name="HiddenFromUser">Whether Jellyfin's parental control hides the channel from the user the list was asked for; absent without a user.</param>
public record LiveTvChannelDto(Guid Id, string Name, string? Number, string? ImageTag, string Type, string? Rating = null, bool? HiddenFromUser = null)
{
    /// <summary>
    /// Maps a channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The DTO.</returns>
    public static LiveTvChannelDto From(LiveTvChannelInfo channel) => From(channel, null);

    /// <summary>
    /// Maps a channel, with whether it is hidden from a user.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="hiddenFromUser">Whether it is hidden from the user, or null when no user was asked for.</param>
    /// <returns>The DTO.</returns>
    public static LiveTvChannelDto From(LiveTvChannelInfo channel, bool? hiddenFromUser)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new(channel.Id, channel.Name, channel.Number, channel.ImageTag, channel.IsRadio ? "Radio" : "TV", channel.Rating, hiddenFromUser);
    }
}
