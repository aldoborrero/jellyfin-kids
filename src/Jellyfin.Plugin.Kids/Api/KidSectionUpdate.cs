namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// A home section in a profile update. The name is a string, not the enum, so that an unknown name is reported as
/// <c>Sections[i]</c> by validation instead of failing JSON binding.
/// </summary>
public class KidSectionUpdate
{
    /// <summary>
    /// Gets the section name (KeepWatching, Picked, Libraries, New or Favourites), matched case-insensitively.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// Gets whether the section is shown (default true).
    /// </summary>
    public bool? Visible { get; init; }
}
