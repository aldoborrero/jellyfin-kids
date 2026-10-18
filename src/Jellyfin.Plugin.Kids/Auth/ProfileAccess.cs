using System;

namespace Jellyfin.Plugin.Kids.Auth;

/// <summary>
/// Who may read and change kid profiles. The single place for these rules (feature 2 adds parent grants here).
/// </summary>
public static class ProfileAccess
{
    /// <summary>
    /// Whether the caller may read a kid's profile: the kid themselves, an administrator or an API key.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <param name="userId">The kid's user id.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool CanRead(CallerContext caller, Guid userId)
        => caller.IsAdmin || (caller.UserId != Guid.Empty && caller.UserId == userId);

    /// <summary>
    /// Whether the caller may mark, unmark or change kids: administrators and API keys only. A kid's own token is
    /// never enough, even for their own profile.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool CanMutate(CallerContext caller) => caller.IsAdmin;
}
