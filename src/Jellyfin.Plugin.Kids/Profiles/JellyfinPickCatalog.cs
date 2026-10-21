using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// <see cref="IPickCatalog"/> backed by Jellyfin's library. Queries run without a user, so every collection and
/// playlist on the server is listed, including other users' private playlists.
/// </summary>
public sealed class JellyfinPickCatalog : IPickCatalog
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPickCatalog"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public JellyfinPickCatalog(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<PickCandidate> Resolve(IEnumerable<Guid> ids)
        // GetItemById throws on an empty id
        => [.. ids.Where(id => id != Guid.Empty).Select(id => ToCandidate(_libraryManager.GetItemById(id))).OfType<PickCandidate>()];

    /// <inheritdoc />
    public IReadOnlyList<PickCandidate> Search(string? term, int limit)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet, BaseItemKind.Playlist],
            Recursive = true,
            // NameContains is matched against CleanName, which is lowercased without diacritics, so clean the term the
            // same way (BaseItemRepository.TranslateQuery, OrderMapper.GetCleanValue)
            NameContains = string.IsNullOrWhiteSpace(term) ? null : term.Trim().RemoveDiacritics().ToLowerInvariant(),
            OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)],
            Limit = limit
        };
        return [.. _libraryManager.GetItemList(query).Select(ToCandidate).OfType<PickCandidate>()];
    }

    private static PickCandidate? ToCandidate(BaseItem? item) => item switch
    {
        BoxSet boxSet => new PickCandidate(boxSet.Id, boxSet.Name, PickType.BoxSet),
        Playlist playlist => new PickCandidate(playlist.Id, playlist.Name, PickType.Playlist),
        _ => null
    };
}
