using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// The server's live TV channels, whoever may watch them.
/// </summary>
public interface ILiveTvChannelCatalog
{
    /// <summary>
    /// Resolves item ids to live TV channels; ids that do not exist or are another kind of item are left out.
    /// </summary>
    /// <param name="ids">The item ids.</param>
    /// <returns>The channels.</returns>
    IReadOnlyList<LiveTvChannelInfo> Resolve(IEnumerable<Guid> ids);

    /// <summary>
    /// Lists every live TV channel, in Jellyfin's channel order.
    /// </summary>
    /// <returns>The channels.</returns>
    IReadOnlyList<LiveTvChannelInfo> List();
}
