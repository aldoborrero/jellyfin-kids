using System;

namespace Jellyfin.Plugin.Kids.Auth;

/// <summary>
/// Who may read and change the household parent lock. The single place for these rules.
/// </summary>
public static class LockAccess
{
    /// <summary>
    /// Whether the caller may read the lock status (type and change time): anyone signed in, kids included.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool CanReadStatus(CallerContext caller) => caller.IsApiKey || caller.UserId != Guid.Empty;

    /// <summary>
    /// Whether the caller may set or replace the lock without the current code: administrators and API keys.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool CanSetWithoutCurrent(CallerContext caller) => caller.IsAdmin;

    /// <summary>
    /// Whether the caller may replace the lock by also sending the current code: holders of a parent grant.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool CanSetWithCurrent(CallerContext caller) => caller.HasGrant;

    /// <summary>
    /// Whether the caller may import a verifier when no lock is set: administrators and API keys only.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool CanImport(CallerContext caller) => caller.IsAdmin;

    /// <summary>
    /// Whether the caller may remove the lock: administrators and API keys only, never a grant.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool CanRemove(CallerContext caller) => caller.IsAdmin;

    /// <summary>
    /// Whether the caller may read the verifier for offline unlock: administrators, API keys and grant holders. A
    /// kid's token alone never gets it, because a short code can be guessed offline from it.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool CanReadVerifier(CallerContext caller) => caller.IsAdmin || caller.HasGrant;
}
