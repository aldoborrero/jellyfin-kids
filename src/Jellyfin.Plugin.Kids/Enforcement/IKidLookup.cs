using System;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Who the kids are, answered from memory: what <see cref="KidRequest"/> needs to tell whether a request is a kid's.
/// </summary>
public interface IKidLookup
{
    /// <summary>
    /// Whether a user is a kid.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns><c>true</c> for a kid.</returns>
    bool IsKid(Guid userId);

    /// <summary>
    /// Finds the kid who owns a current session on a device, for requests that carry no user.
    /// </summary>
    /// <param name="deviceId">The device id from the request.</param>
    /// <returns>The kid, or <see cref="Guid.Empty"/>.</returns>
    Guid KidForDevice(string deviceId);
}
