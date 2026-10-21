using System;
using System.Globalization;
using MediaBrowser.Common.Extensions;

namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// Reads which channel Jellyfin's live TV ids name (research R1). Pure.
/// </summary>
public static class LiveTvIds
{
    // Jellyfin prefixes the live stream ids and open tokens of each media source provider with the MD5 of the
    // provider's type name (MediaSourceManager.SetKeyProperties); live TV's is LiveTvMediaSourceProvider
    private const string LiveTvProviderType = "Jellyfin.LiveTv.LiveTvMediaSourceProvider";

    /// <summary>
    /// Gets the prefix of every live TV live stream id and open token.
    /// </summary>
    public static string ProviderPrefix { get; } = LiveTvProviderType.GetMD5().ToString("N", CultureInfo.InvariantCulture) + "_";

    /// <summary>
    /// Whether a live stream id was opened by Jellyfin's live TV, as opposed to another provider (internet channels).
    /// </summary>
    /// <param name="liveStreamId">The live stream id.</param>
    /// <returns><c>true</c> for a live TV stream.</returns>
    public static bool IsLiveTvStreamId(string? liveStreamId)
        => liveStreamId is not null && liveStreamId.StartsWith(ProviderPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an open token is one Jellyfin hands to its live TV provider (the provider prefix).
    /// </summary>
    /// <param name="openToken">The open token.</param>
    /// <returns><c>true</c> for a live TV open token.</returns>
    public static bool IsLiveTvOpenToken(string? openToken)
        => openToken is not null && openToken.StartsWith(ProviderPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the item a live TV open token opens, as Jellyfin does: after the provider prefix the token is
    /// <c>&lt;type&gt;_&lt;item id&gt;_&lt;source&gt;</c>, and <c>LiveTvMediaSourceProvider.OpenMediaSource</c> opens the second
    /// part as the channel whatever the first says, in any GUID format.
    /// </summary>
    /// <param name="openToken">The open token.</param>
    /// <returns>The item id, or null when the token is not a live TV one or names no item.</returns>
    public static Guid? ItemFromOpenToken(string? openToken)
    {
        if (!IsLiveTvOpenToken(openToken))
        {
            return null;
        }

        var keys = openToken![ProviderPrefix.Length..].Split('_', 3);
        return keys.Length >= 2 && Guid.TryParse(keys[1], out var id) && id != Guid.Empty ? id : null;
    }
}
