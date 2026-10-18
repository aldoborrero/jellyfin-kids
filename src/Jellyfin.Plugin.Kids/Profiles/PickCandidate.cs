using System;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// A collection or playlist that can be picked.
/// </summary>
/// <param name="Id">The item id.</param>
/// <param name="Name">The item name.</param>
/// <param name="Type">Whether it is a collection or a playlist.</param>
public sealed record PickCandidate(Guid Id, string Name, PickType Type);
