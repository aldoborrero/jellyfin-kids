using System;
using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// A collection or playlist the dashboard editor can offer as a pick.
/// </summary>
/// <param name="Id">The item id.</param>
/// <param name="Name">The item name.</param>
/// <param name="Type"><c>BoxSet</c> or <c>Playlist</c>.</param>
public record PickCandidateDto(Guid Id, string Name, PickType Type)
{
    /// <summary>
    /// Maps a candidate.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    /// <returns>The DTO.</returns>
    public static PickCandidateDto From(PickCandidate candidate) => new(candidate.Id, candidate.Name, candidate.Type);
}
