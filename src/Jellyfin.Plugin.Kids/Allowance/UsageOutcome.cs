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
    NotAKid
}
