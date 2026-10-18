using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.ParentLock;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Kids.Auth;

/// <summary>
/// Builds the <see cref="CallerContext"/> for a request: the caller, their registered device and whether they carry a
/// valid parent grant.
/// </summary>
public sealed class CallerResolver
{
    /// <summary>
    /// The request header that carries a parent grant. Grants are never read from the query string, so they stay out
    /// of access logs.
    /// </summary>
    public const string GrantHeader = "X-Kids-Grant";

    private readonly IAuthorizationContext _authorizationContext;
    private readonly IDeviceDirectory _devices;
    private readonly GrantService _grants;

    /// <summary>
    /// Initializes a new instance of the <see cref="CallerResolver"/> class.
    /// </summary>
    /// <param name="authorizationContext">Jellyfin's authorization context.</param>
    /// <param name="devices">The registered devices.</param>
    /// <param name="grants">The parent grants.</param>
    public CallerResolver(IAuthorizationContext authorizationContext, IDeviceDirectory devices, GrantService grants)
    {
        _authorizationContext = authorizationContext;
        _devices = devices;
        _grants = grants;
    }

    /// <summary>
    /// Resolves the caller of a request.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="needDevice">
    /// Whether the device is needed even without a grant (verifying a code). Otherwise it is only looked up when the
    /// request carries a grant, so ordinary reads pay nothing.
    /// </param>
    /// <returns>The caller.</returns>
    public async Task<CallerContext> ResolveAsync(HttpContext httpContext, bool needDevice = false)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var info = await _authorizationContext.GetAuthorizationInfo(httpContext).ConfigureAwait(false);
        var caller = CallerContext.From(info);
        var grant = httpContext.Request.Headers[GrantHeader].ToString();
        if ((!needDevice && grant.Length == 0) || !info.IsAuthenticated || string.IsNullOrEmpty(info.Token))
        {
            return caller;
        }

        var deviceKey = DeviceKeyFor(info.IsApiKey, info.Token, _devices);
        var hasGrant = deviceKey is not null && grant.Length > 0 && _grants.IsValid(grant, caller.UserId, deviceKey);
        return caller with { DeviceKey = deviceKey, HasGrant = hasGrant };
    }

    /// <summary>
    /// The device key for a token: the device Jellyfin registered for it, or a hash of an API key (which has no
    /// device). The two forms are prefixed so they never collide.
    /// </summary>
    /// <param name="isApiKey">Whether the token is an API key.</param>
    /// <param name="token">The token.</param>
    /// <param name="devices">The registered devices.</param>
    /// <returns>The device key, or null if a user token has no registered device.</returns>
    internal static string? DeviceKeyFor(bool isApiKey, string token, IDeviceDirectory devices)
    {
        if (isApiKey)
        {
            return "apikey:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        var deviceId = devices.GetRegisteredDeviceId(token);
        return string.IsNullOrEmpty(deviceId) ? null : "device:" + deviceId;
    }
}
