using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// The result of saving a kid profile.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Profile">The stored profile when saved, or the current one on a conflict.</param>
/// <param name="Errors">The validation errors keyed by field, when invalid.</param>
public sealed record SaveResult(SaveOutcome Outcome, KidProfile? Profile = null, IReadOnlyDictionary<string, string[]>? Errors = null);
