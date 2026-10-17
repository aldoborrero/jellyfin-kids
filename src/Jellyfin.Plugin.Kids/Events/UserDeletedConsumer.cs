using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Kids.Profiles;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Events;

/// <summary>
/// Removes a kid's profile when their Jellyfin user is deleted.
/// </summary>
public sealed class UserDeletedConsumer : IEventConsumer<UserDeletedEventArgs>
{
    private readonly KidProfileService _profiles;
    private readonly ILogger<UserDeletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDeletedConsumer"/> class.
    /// </summary>
    /// <param name="profiles">The kid profile service.</param>
    /// <param name="logger">The logger.</param>
    public UserDeletedConsumer(KidProfileService profiles, ILogger<UserDeletedConsumer> logger)
    {
        _profiles = profiles;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        var user = eventArgs.Argument;
        if (await _profiles.DeleteAsync(user.Id).ConfigureAwait(false))
        {
            _logger.LogInformation("Removed the kid profile of deleted user {UserName}", user.Username);
        }
    }
}
