using Jellyfin.Data.Queries;
using MediaBrowser.Controller.Devices;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// Finds a token's registered device through Jellyfin's device manager.
/// </summary>
/// <remarks>
/// The <c>DeviceId</c> in the request's <c>Authorization</c> header is deliberately not used: Jellyfin takes it from
/// the header and only falls back to the token's device when the header has none
/// (<c>Jellyfin.Server.Implementations/Security/AuthorizationContext.cs:133-147</c>), so a token can claim any device
/// on each request. The token's own device row is what Jellyfin stored at sign-in.
/// </remarks>
public sealed class JellyfinDeviceDirectory : IDeviceDirectory
{
    private readonly IDeviceManager _deviceManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinDeviceDirectory"/> class.
    /// </summary>
    /// <param name="deviceManager">Jellyfin's device manager.</param>
    public JellyfinDeviceDirectory(IDeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
    }

    /// <inheritdoc />
    public string? GetRegisteredDeviceId(string token)
    {
        var devices = _deviceManager.GetDevices(new DeviceQuery { AccessToken = token }).Items;
        return devices.Count > 0 ? devices[0].DeviceId : null;
    }
}
