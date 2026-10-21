using System;

namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// What the live TV rules need to know about Jellyfin's items and open live streams.
/// </summary>
public interface ILiveTvItems
{
    /// <summary>
    /// Finds the live TV channel an item is or belongs to: a channel is its own, a guide program has its channel.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The channel id, or null when the item is not live TV (or does not exist).</returns>
    Guid? ChannelOf(Guid itemId);

    /// <summary>
    /// Whether a live stream is still open.
    /// </summary>
    /// <param name="liveStreamId">The live stream id.</param>
    /// <returns><c>true</c> while it is open.</returns>
    bool IsOpen(string liveStreamId);

    /// <summary>
    /// Finds the unique id of an open live stream, the id its <c>LiveTv/LiveStreamFiles</c> URL carries.
    /// </summary>
    /// <param name="liveStreamId">The live stream id.</param>
    /// <returns>The unique id, or null.</returns>
    string? UniqueIdOf(string liveStreamId);
}
