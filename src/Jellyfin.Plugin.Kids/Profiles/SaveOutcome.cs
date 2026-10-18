namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// What saving a kid profile did.
/// </summary>
public enum SaveOutcome
{
    /// <summary>The profile was created or updated.</summary>
    Saved,

    /// <summary>The Jellyfin user does not exist (create) or is not a kid (update).</summary>
    NotFound,

    /// <summary>The user is already a kid (create) or the base revision is stale (update); nothing changed.</summary>
    Conflict,

    /// <summary>The settings are invalid; nothing changed.</summary>
    Invalid
}
