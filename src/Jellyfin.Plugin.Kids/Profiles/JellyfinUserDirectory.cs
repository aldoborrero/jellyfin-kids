using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// <see cref="IKidsUserDirectory"/> backed by Jellyfin's user manager.
/// </summary>
public sealed class JellyfinUserDirectory : IKidsUserDirectory
{
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinUserDirectory"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    public JellyfinUserDirectory(IUserManager userManager)
    {
        _userManager = userManager;
    }

    /// <inheritdoc />
    public bool UserExists(Guid userId) => _userManager.GetUserById(userId) is not null;

    /// <inheritdoc />
    public IReadOnlyCollection<Guid> GetAllUserIds() => [.. _userManager.GetUsersIds()];
}
