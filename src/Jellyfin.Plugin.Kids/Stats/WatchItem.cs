using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.LiveTv;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// What a watch log segment played (feature 22): an episode, a movie or other item, or a live TV channel.
/// </summary>
/// <param name="ItemId">The item, or null when the event named none.</param>
/// <param name="ItemType">Jellyfin's item kind, such as <c>Episode</c>, <c>Movie</c> or <c>TvChannel</c>.</param>
/// <param name="ItemName">The item's name: the episode title, the movie or the channel.</param>
/// <param name="SeriesId">The series of an episode.</param>
/// <param name="SeriesName">The series' name.</param>
/// <param name="Season">The season number of an episode.</param>
/// <param name="Episode">The episode number.</param>
/// <param name="ChannelId">The live TV channel, for live TV.</param>
/// <param name="ChannelName">The channel's name, when known.</param>
public sealed record WatchItem(
    Guid? ItemId,
    string? ItemType,
    string? ItemName,
    Guid? SeriesId = null,
    string? SeriesName = null,
    int? Season = null,
    int? Episode = null,
    Guid? ChannelId = null,
    string? ChannelName = null)
{
    /// <summary>Gets an item nothing is known about.</summary>
    public static WatchItem Unknown { get; } = new(null, null, null);

    /// <summary>Gets a value indicating whether this is live TV.</summary>
    public bool IsLiveTv => ChannelId is not null;

    /// <summary>
    /// Reads what a playback event plays, from the item's stored properties only: no library lookup, so it is safe in
    /// the session manager's event handlers.
    /// </summary>
    /// <param name="item">The event's item, or null.</param>
    /// <returns>The item.</returns>
    public static WatchItem From(BaseItem? item)
    {
        if (item is null)
        {
            return Unknown;
        }

        var kind = item.GetBaseItemKind().ToString();
        return item switch
        {
            LiveTvChannel channel => new WatchItem(channel.Id, kind, channel.Name, ChannelId: channel.Id, ChannelName: channel.Name),
            LiveTvProgram program => new WatchItem(program.Id, kind, program.Name, ChannelId: NotEmpty(program.ChannelId)),
            Episode episode => new WatchItem(
                episode.Id,
                kind,
                episode.Name,
                NotEmpty(episode.SeriesId),
                episode.SeriesName,
                episode.ParentIndexNumber,
                episode.IndexNumber),
            _ => new WatchItem(NotEmpty(item.Id), kind, item.Name)
        };
    }

    private static Guid? NotEmpty(Guid id) => id == Guid.Empty ? null : id;
}
