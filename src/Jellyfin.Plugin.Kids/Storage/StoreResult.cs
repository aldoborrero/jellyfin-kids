using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// The result of a store write.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Profile">The stored profile after the write, or the current one on a refusal; null if there is none.</param>
public sealed record StoreResult(StoreOutcome Outcome, KidProfile? Profile);
