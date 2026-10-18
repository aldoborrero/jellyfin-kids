using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Kids.ParentLock;
using Jellyfin.Plugin.Kids.Profiles;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Events;

/// <summary>
/// Removes a kid's profile, and any parent grants the user holds, when their Jellyfin user is deleted.
/// </summary>
public sealed class UserDeletedConsumer : IEventConsumer<UserDeletedEventArgs>
{
    private readonly KidProfileService _profiles;
    private readonly GrantService _grants;
    private readonly ILogger<UserDeletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDeletedConsumer"/> class.
    /// </summary>
    /// <param name="profiles">The kid profile service.</param>
    /// <param name="grants">The parent grants.</param>
    /// <param name="logger">The logger.</param>
    public UserDeletedConsumer(KidProfileService profiles, GrantService grants, ILogger<UserDeletedConsumer> logger)
    {
        _profiles = profiles;
        _grants = grants;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        var user = eventArgs.Argument;
        _grants.RevokeUser(user.Id);
        if (await _profiles.DeleteAsync(user.Id).ConfigureAwait(false))
        {
            _logger.LogInformation("Removed the kid profile of deleted user {UserName}", user.Username);
        }
    }
}
