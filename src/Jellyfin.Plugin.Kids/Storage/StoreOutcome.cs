namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// What a store write did.
/// </summary>
public enum StoreOutcome
{
    /// <summary>The profile was created.</summary>
    Created,

    /// <summary>The profile was updated.</summary>
    Updated,

    /// <summary>The user is already a kid; nothing changed.</summary>
    AlreadyExists,

    /// <summary>The base revision is not the current one; nothing changed.</summary>
    Conflict,

    /// <summary>The user is not a kid.</summary>
    NotFound
}
