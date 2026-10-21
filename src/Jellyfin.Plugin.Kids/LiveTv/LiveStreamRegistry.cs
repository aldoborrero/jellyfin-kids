using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// Which channel each live stream a kid opened plays (research R2): the plugin records it when it lets a kid open a
/// channel, so later requests that only name the stream are judged by its channel. In memory, like Jellyfin's open
/// live streams.
/// </summary>
public sealed class LiveStreamRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets how many ids are known (live stream ids and unique ids).
    /// </summary>
    public int Count => _byId.Count;

    /// <summary>
    /// Records an opened live stream, and forgets the streams that are no longer open.
    /// </summary>
    /// <param name="liveStreamId">The live stream id (<c>MediaSourceInfo.LiveStreamId</c>).</param>
    /// <param name="uniqueId">The stream's unique id (the <c>LiveTv/LiveStreamFiles/{id}</c> part), if known.</param>
    /// <param name="channelId">The channel it plays.</param>
    /// <param name="isOpen">Whether a live stream id is still open.</param>
    public void Record(string liveStreamId, string? uniqueId, Guid channelId, Func<string, bool> isOpen)
    {
        ArgumentException.ThrowIfNullOrEmpty(liveStreamId);
        ArgumentNullException.ThrowIfNull(isOpen);
        foreach (var (key, entry) in _byId.ToArray())
        {
            if (!string.Equals(entry.LiveStreamId, liveStreamId, StringComparison.OrdinalIgnoreCase) && !isOpen(entry.LiveStreamId))
            {
                _byId.TryRemove(key, out _);
            }
        }

        var recorded = new Entry(liveStreamId, channelId);
        _byId[liveStreamId] = recorded;
        if (!string.IsNullOrEmpty(uniqueId))
        {
            _byId[uniqueId] = recorded;
        }
    }

    /// <summary>
    /// Finds the channel of a recorded live stream id or unique id.
    /// </summary>
    /// <param name="id">The live stream id or unique id.</param>
    /// <param name="channelId">The channel.</param>
    /// <returns><c>true</c> if the stream is known.</returns>
    public bool TryGetChannel(string id, out Guid channelId)
    {
        if (!string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var entry))
        {
            channelId = entry.ChannelId;
            return true;
        }

        channelId = Guid.Empty;
        return false;
    }

    private sealed record Entry(string LiveStreamId, Guid ChannelId);
}
