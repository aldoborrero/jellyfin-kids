using System;
using System.Collections.Concurrent;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;

namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// <see cref="ILiveTvItems"/> over Jellyfin's library and media sources. An item never changes kind, so answers for
/// existing items are kept (bounded) to spare the lookups on every HLS segment.
/// </summary>
public sealed class JellyfinLiveTvItems : ILiveTvItems
{
    private const int MaxRemembered = 4096;

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ConcurrentDictionary<Guid, Guid> _channels = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinLiveTvItems"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="mediaSourceManager">The media source manager.</param>
    public JellyfinLiveTvItems(ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
    }

    /// <inheritdoc />
    public Guid? ChannelOf(Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            return null;
        }

        // Guid.Empty stands for "an item that is not live TV"
        if (_channels.TryGetValue(itemId, out var known))
        {
            return known == Guid.Empty ? null : known;
        }

        Guid channel;
        switch (_libraryManager.GetItemById(itemId))
        {
            case null:
                // Not remembered: a channel may appear once the guide is refreshed
                return null;
            case LiveTvChannel:
                channel = itemId;
                break;
            case LiveTvProgram program:
                channel = program.ChannelId;
                break;
            default:
                channel = Guid.Empty;
                break;
        }

        if (_channels.Count >= MaxRemembered)
        {
            _channels.Clear();
        }

        _channels[itemId] = channel;
        return channel == Guid.Empty ? null : channel;
    }

    /// <inheritdoc />
    public bool IsOpen(string liveStreamId)
        => !string.IsNullOrEmpty(liveStreamId) && _mediaSourceManager.GetLiveStreamInfo(liveStreamId) is not null;

    /// <inheritdoc />
    public string? UniqueIdOf(string liveStreamId)
        => string.IsNullOrEmpty(liveStreamId) ? null : _mediaSourceManager.GetLiveStreamInfo(liveStreamId)?.UniqueId;
}
