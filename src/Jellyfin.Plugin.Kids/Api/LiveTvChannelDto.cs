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
public record LiveTvChannelDto(Guid Id, string Name, string? Number, string? ImageTag, string Type)
{
    /// <summary>
    /// Maps a channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The DTO.</returns>
    public static LiveTvChannelDto From(LiveTvChannelInfo channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new(channel.Id, channel.Name, channel.Number, channel.ImageTag, channel.IsRadio ? "Radio" : "TV");
    }
}
