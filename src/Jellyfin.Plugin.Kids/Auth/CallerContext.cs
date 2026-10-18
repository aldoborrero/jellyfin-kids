using System;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Kids.Auth;

/// <summary>
/// Who is calling the plugin API.
/// </summary>
/// <param name="UserId">The signed-in user, or <see cref="Guid.Empty"/> for API keys and anonymous calls.</param>
/// <param name="IsAdmin">Whether the caller is an administrator or an API key.</param>
/// <param name="IsApiKey">Whether the caller uses an API key.</param>
/// <param name="DeviceKey">
/// The caller's device as Jellyfin registered it for the token (never the header's claim), or a hash of the API key;
/// null when it was not needed or cannot be found (see <see cref="CallerResolver"/>).
/// </param>
/// <param name="HasGrant">Whether the request carries a valid parent grant for this user and device.</param>
/// <param name="UserName">The user name, for logs.</param>
public sealed record CallerContext(
    Guid UserId,
    bool IsAdmin,
    bool IsApiKey,
    string? DeviceKey = null,
    bool HasGrant = false,
    string? UserName = null)
{
    /// <summary>
    /// Gets the key wrong codes are counted under: the user, or the API key; null for anonymous callers.
    /// </summary>
    public string? ThrottleUserKey => IsApiKey ? DeviceKey : UserId == Guid.Empty ? null : UserId.ToString("N");

    /// <summary>
    /// Gets how the caller is named in logs: the user name, or "API key". Never a key or a hash.
    /// </summary>
    public string LogName => IsApiKey ? "API key" : UserName ?? UserId.ToString("N");

    /// <summary>
    /// Reads the caller from Jellyfin's authorization context (a public API, unlike the internal claim names),
    /// without a device or a grant.
    /// </summary>
    /// <param name="authorizationContext">Jellyfin's authorization context.</param>
    /// <param name="httpContext">The request.</param>
    /// <returns>The caller.</returns>
    public static async Task<CallerContext> FromAsync(IAuthorizationContext authorizationContext, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(authorizationContext);
        var info = await authorizationContext.GetAuthorizationInfo(httpContext).ConfigureAwait(false);
        return From(info);
    }

    /// <summary>
    /// Builds the caller from Jellyfin's authorization info, without a device or a grant.
    /// </summary>
    /// <param name="info">The authorization info.</param>
    /// <returns>The caller.</returns>
    internal static CallerContext From(AuthorizationInfo info)
    {
        var isAdmin = info.IsApiKey || info.User?.HasPermission(PermissionKind.IsAdministrator) == true;
        return new CallerContext(info.User?.Id ?? Guid.Empty, isAdmin, info.IsApiKey, UserName: info.User?.Username);
    }
}
