namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// What a screen-time change did.
/// </summary>
public enum UsageOutcome
{
    /// <summary>The change was stored.</summary>
    Saved,

    /// <summary>The request was invalid; nothing changed.</summary>
    Invalid,

    /// <summary>The user is not a kid; nothing changed.</summary>
    NotAKid,

    /// <summary>The kid has no bonus with that id (feature 13); nothing changed.</summary>
    NotFound,

    /// <summary>The bonus can no longer be undone (feature 13); nothing changed.</summary>
    TooLate
}
