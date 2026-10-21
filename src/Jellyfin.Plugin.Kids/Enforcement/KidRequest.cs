using System;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Tells which kid a request is for, shared by the playback gate (feature 5) and the live TV rules (feature 6).
/// </summary>
public static class KidRequest
{
    /// <summary>Jellyfin.Api's <c>InternalClaimTypes.UserId</c>; that assembly is not a package plugins can reference.</summary>
    public const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>
    /// The kid a request is for: its signed-in user, or for a request without one, the kid who owns a session on the
    /// device it names. Empty for administrators and API keys (both have the Administrator role) and anyone else.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="kids">The kids.</param>
    /// <param name="claimMissing">Set when the request is signed in but carries no user id claim, which the caller logs once.</param>
    /// <returns>The kid, or <see cref="Guid.Empty"/>.</returns>
    public static Guid Resolve(HttpContext httpContext, IKidLookup kids, out bool claimMissing)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(kids);
        claimMissing = false;
        var user = httpContext.User;
        if (user.IsInRole("Administrator"))
        {
            return Guid.Empty;
        }

        var claim = user.FindFirst(UserIdClaim)?.Value;
        if (Guid.TryParse(claim, out var userId) && userId != Guid.Empty)
        {
            return kids.IsKid(userId) ? userId : Guid.Empty;
        }

        claimMissing = user.Identity?.IsAuthenticated == true && claim is null;
        var deviceId = httpContext.Request.Query["deviceId"].ToString();
        return deviceId.Length == 0 ? Guid.Empty : kids.KidForDevice(deviceId);
    }
}
