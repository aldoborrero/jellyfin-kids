namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// A home section as sent by a client, before validation.
/// </summary>
/// <param name="Section">The section name, matched case-insensitively.</param>
/// <param name="Visible">Whether the section is shown; defaults to true.</param>
public sealed record SectionInput(string? Section, bool? Visible);
