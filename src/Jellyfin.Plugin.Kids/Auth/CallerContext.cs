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
public sealed record CallerContext(Guid UserId, bool IsAdmin, bool IsApiKey)
{
    /// <summary>
    /// Reads the caller from Jellyfin's authorization context (a public API, unlike the internal claim names).
    /// </summary>
    /// <param name="authorizationContext">Jellyfin's authorization context.</param>
    /// <param name="httpContext">The request.</param>
    /// <returns>The caller.</returns>
    public static async Task<CallerContext> FromAsync(IAuthorizationContext authorizationContext, HttpContext httpContext)
    {
        var info = await authorizationContext.GetAuthorizationInfo(httpContext).ConfigureAwait(false);
        var isAdmin = info.IsApiKey || info.User?.HasPermission(PermissionKind.IsAdministrator) == true;
        return new CallerContext(info.User?.Id ?? Guid.Empty, isAdmin, info.IsApiKey);
    }
}
