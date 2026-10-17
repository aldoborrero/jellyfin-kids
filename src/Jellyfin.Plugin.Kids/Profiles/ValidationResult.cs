using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// The result of validating kid profile settings.
/// </summary>
/// <param name="Settings">The normalised settings, or null when invalid.</param>
/// <param name="Errors">The errors keyed by field (for example <c>Sections[1]</c>); empty when valid.</param>
public sealed record ValidationResult(KidProfileSettings? Settings, IReadOnlyDictionary<string, string[]> Errors)
{
    /// <summary>
    /// Gets a value indicating whether the settings are valid.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}
