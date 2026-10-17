using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// The Jellyfin users, as far as kid profiles need them.
/// </summary>
public interface IKidsUserDirectory
{
    /// <summary>
    /// Whether a Jellyfin user exists.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns><c>true</c> if the user exists.</returns>
    bool UserExists(Guid userId);

    /// <summary>
    /// Gets the ids of every Jellyfin user.
    /// </summary>
    /// <returns>The user ids.</returns>
    IReadOnlyCollection<Guid> GetAllUserIds();
}
