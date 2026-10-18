namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// Finds the device Jellyfin registered for an access token.
/// </summary>
public interface IDeviceDirectory
{
    /// <summary>
    /// Gets the device id Jellyfin stored when the token was issued.
    /// </summary>
    /// <param name="token">The caller's access token.</param>
    /// <returns>The registered device id, or null if the token has no device (for example an API key).</returns>
    string? GetRegisteredDeviceId(string token);
}
