using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Storage;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// Marks, unmarks, reads and changes kid profiles. Access rules are the caller's job (<c>ProfileAccess</c>).
/// </summary>
public sealed class KidProfileService
{
    private readonly KidProfileStore _store;
    private readonly IKidsUserDirectory _users;
    private readonly IPickCatalog _catalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="KidProfileService"/> class.
    /// </summary>
    /// <param name="store">The profile store.</param>
    /// <param name="users">The Jellyfin users.</param>
    /// <param name="catalog">The server's collections and playlists.</param>
    public KidProfileService(KidProfileStore store, IKidsUserDirectory users, IPickCatalog catalog)
    {
        _store = store;
        _users = users;
        _catalog = catalog;
    }

    /// <summary>
    /// Gets a kid's profile.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The profile, or null if the user is not a kid.</returns>
    public Task<KidProfile?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        => _store.GetAsync(userId, cancellationToken);

    /// <summary>
    /// Lists the kids and their profile revisions.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The kids.</returns>
    public Task<IReadOnlyList<KidSummary>> ListAsync(CancellationToken cancellationToken = default)
        => _store.ListAsync(cancellationToken);

    /// <summary>
    /// Marks a user as a kid (<paramref name="baseRevision"/> 0) or replaces a kid's settings (the revision they are
    /// based on). Omitted settings take their defaults.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="baseRevision">0 to create, otherwise the revision the change is based on.</param>
    /// <param name="input">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome, with the stored or current profile, or the validation errors.</returns>
    public async Task<SaveResult> SaveAsync(Guid userId, long baseRevision, KidProfileInput input, CancellationToken cancellationToken = default)
    {
        if (baseRevision < 0)
        {
            return new SaveResult(SaveOutcome.Invalid, Errors: new Dictionary<string, string[]> { ["Revision"] = ["Must be 0 or more."] });
        }

        var current = await _store.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (baseRevision == 0)
        {
            if (current is not null)
            {
                return new SaveResult(SaveOutcome.Conflict, current);
            }

            if (!_users.UserExists(userId))
            {
                return new SaveResult(SaveOutcome.NotFound);
            }
        }
        else if (current is null)
        {
            return new SaveResult(SaveOutcome.NotFound);
        }
        else if (current.Revision != baseRevision)
        {
            return new SaveResult(SaveOutcome.Conflict, current);
        }

        var validation = KidProfileValidator.Validate(input, current?.Settings.PickedItemIds ?? [], _catalog);
        if (!validation.IsValid)
        {
            return new SaveResult(SaveOutcome.Invalid, Errors: validation.Errors);
        }

        var stored = baseRevision == 0
            ? await _store.InsertAsync(userId, validation.Settings!, cancellationToken).ConfigureAwait(false)
            : await _store.UpdateIfRevisionAsync(userId, baseRevision, validation.Settings!, cancellationToken).ConfigureAwait(false);
        return stored.Outcome switch
        {
            StoreOutcome.Created or StoreOutcome.Updated => new SaveResult(SaveOutcome.Saved, stored.Profile),
            StoreOutcome.AlreadyExists or StoreOutcome.Conflict => new SaveResult(SaveOutcome.Conflict, stored.Profile),
            _ => new SaveResult(SaveOutcome.NotFound)
        };
    }

    /// <summary>
    /// Unmarks a kid, deleting their settings.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the user was a kid.</returns>
    public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
        => _store.DeleteAsync(userId, cancellationToken);
}
