using System;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// One stretch of a kid watching one item on one device, as the watch log keeps it (feature 22, data-model.md).
/// </summary>
/// <param name="Id">The segment's id.</param>
/// <param name="PlaybackId">The playback it belongs to: the segments a pause or a new day split from one playback share it.</param>
/// <param name="UserId">The kid.</param>
/// <param name="Day">The household day, <c>yyyy-MM-dd</c>; a segment never crosses a day start.</param>
/// <param name="StartedAt">When the first counted second began.</param>
/// <param name="EndedAt">When the last counted second ended.</param>
/// <param name="Seconds">The counted playing time, as for the daily allowance (pauses excluded).</param>
/// <param name="Item">What played.</param>
/// <param name="Device">The device name.</param>
/// <param name="Client">The app name.</param>
public sealed record WatchSegment(
    string Id,
    string PlaybackId,
    Guid UserId,
    string Day,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int Seconds,
    WatchItem Item,
    string? Device,
    string? Client);
