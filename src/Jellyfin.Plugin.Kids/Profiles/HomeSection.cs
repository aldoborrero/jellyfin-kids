namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// A section of the kids home screen. The declaration order is the default order, matching Wholphin's
/// <c>KidsHomeSection</c>. Adding a value is a contract change (raises the API version).
/// </summary>
public enum HomeSection
{
    /// <summary>Items the kid has started watching.</summary>
    KeepWatching,

    /// <summary>Collections and playlists picked by the grown-ups.</summary>
    Picked,

    /// <summary>The kid's libraries.</summary>
    Libraries,

    /// <summary>Recently added items.</summary>
    New,

    /// <summary>The kid's favourites.</summary>
    Favourites
}
