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

    /// <summary>
    /// Gets the channels Jellyfin lists for a user, after its parental control (maximum rating, blocked unrated items,
    /// blocked and allowed tags): the others are hidden from that user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The ids of the channels the user can see.</returns>
    IReadOnlySet<Guid> VisibleTo(Guid userId);
}
