using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Hosted;

/// <summary>
/// At startup, creates or migrates the kids database and removes the profiles of users deleted while the plugin was
/// not running (the user-deleted event only reaches a running plugin).
/// </summary>
/// <remarks>
/// Hosted services start after the plugin instances exist and the server's migrations ran, so users can be read here.
/// </remarks>
public sealed class OrphanProfileSweeper : IHostedService
{
    private readonly KidsDatabase _database;
    private readonly KidProfileStore _store;
    private readonly IKidsUserDirectory _users;
    private readonly ILogger<OrphanProfileSweeper> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrphanProfileSweeper"/> class.
    /// </summary>
    /// <param name="database">The plugin database.</param>
    /// <param name="store">The profile store.</param>
    /// <param name="users">The Jellyfin users.</param>
    /// <param name="logger">The logger.</param>
    public OrphanProfileSweeper(KidsDatabase database, KidProfileStore store, IKidsUserDirectory users, ILogger<OrphanProfileSweeper> logger)
    {
        _database = database;
        _store = store;
        _users = users;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_database.EnsureInitialized())
        {
            return;
        }

        // No users yet (startup wizard not finished): nothing to compare against, and no kids either
        var users = _users.GetAllUserIds().ToHashSet();
        if (users.Count == 0)
        {
            return;
        }

        var kids = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var orphans = kids.Select(k => k.UserId).Where(id => !users.Contains(id)).ToList();
        if (orphans.Count == 0)
        {
            return;
        }

        var removed = await _store.DeleteManyAsync(orphans, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Removed {Count} orphaned kid profile(s)", removed);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
