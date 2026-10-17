using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// The server's collections and playlists, whoever owns them.
/// </summary>
public interface IPickCatalog
{
    /// <summary>
    /// Resolves item ids to collections and playlists; ids that do not exist or are another kind of item are left out.
    /// </summary>
    /// <param name="ids">The item ids.</param>
    /// <returns>The resolved items.</returns>
    IReadOnlyList<PickCandidate> Resolve(IEnumerable<Guid> ids);

    /// <summary>
    /// Lists collections and playlists by name.
    /// </summary>
    /// <param name="term">Text the name must contain, or null for all.</param>
    /// <param name="limit">The most items to return.</param>
    /// <returns>The items, ordered by name.</returns>
    IReadOnlyList<PickCandidate> Search(string? term, int limit);
}
