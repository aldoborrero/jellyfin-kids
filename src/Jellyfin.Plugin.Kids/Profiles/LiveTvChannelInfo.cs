using System;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// A live TV channel of the server, as the kid editor lists it.
/// </summary>
/// <param name="Id">The channel item id.</param>
/// <param name="Name">The channel name.</param>
/// <param name="Number">The channel number, if any.</param>
/// <param name="ImageTag">The tag of the channel logo, or null without one.</param>
/// <param name="IsRadio">Whether it is a radio channel.</param>
/// <param name="Rating">The rating Jellyfin's parental control compares (the custom rating, else the official one), or null when unrated.</param>
public sealed record LiveTvChannelInfo(Guid Id, string Name, string? Number, string? ImageTag, bool IsRadio, string? Rating = null);
