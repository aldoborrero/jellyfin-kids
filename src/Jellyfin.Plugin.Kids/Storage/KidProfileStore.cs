using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Profiles;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// Reads and writes kid profiles, with optimistic revisions.
/// </summary>
public sealed class KidProfileStore
{
    // Unknown properties are ignored and missing ones keep their defaults, so later features can add settings
    // without a table migration
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly KidsDatabase _database;

    /// <summary>
    /// Initializes a new instance of the <see cref="KidProfileStore"/> class.
    /// </summary>
    /// <param name="database">The plugin database.</param>
    public KidProfileStore(KidsDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Gets a kid's profile.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The profile, or null if the user is not a kid.</returns>
    public async Task<KidProfile?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        using var connection = _database.OpenConnection();
        return await ReadAsync(connection, null, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists every kid with their profile revision.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The kids.</returns>
    public async Task<IReadOnlyList<KidSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id, revision FROM kid_profile";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var kids = new List<KidSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            kids.Add(new KidSummary(Guid.ParseExact(reader.GetString(0), "N"), reader.GetInt64(1)));
        }

        return kids;
    }

    /// <summary>
    /// Marks a user as a kid.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="settings">The settings to store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see cref="StoreOutcome.Created"/>, or <see cref="StoreOutcome.AlreadyExists"/> with the current profile.</returns>
    public Task<StoreResult> InsertAsync(Guid userId, KidProfileSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO kid_profile (user_id, revision, settings, created_at, updated_at)
                    VALUES ($id, 1, $settings, $now, $now)
                    ON CONFLICT (user_id) DO NOTHING
                    """;
                command.Parameters.AddWithValue("$id", Key(userId));
                command.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(settings, _jsonOptions));
                command.Parameters.AddWithValue("$now", Now());
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
                {
                    return new StoreResult(StoreOutcome.Created, new KidProfile(userId, 1, settings));
                }

                var current = await ReadAsync(connection, transaction, userId, cancellationToken).ConfigureAwait(false);
                return new StoreResult(StoreOutcome.AlreadyExists, current);
            },
            cancellationToken);
    }

    /// <summary>
    /// Replaces a kid's settings if their profile is still at <paramref name="baseRevision"/>.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="baseRevision">The revision the change is based on.</param>
    /// <param name="settings">The new settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see cref="StoreOutcome.Updated"/>, <see cref="StoreOutcome.Conflict"/> with the current profile, or
    /// <see cref="StoreOutcome.NotFound"/>.
    /// </returns>
    public Task<StoreResult> UpdateIfRevisionAsync(Guid userId, long baseRevision, KidProfileSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                // The revision check and the write are one statement, so only one of two changes on the same
                // revision can win
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE kid_profile SET revision = revision + 1, settings = $settings, updated_at = $now
                    WHERE user_id = $id AND revision = $base
                    """;
                command.Parameters.AddWithValue("$id", Key(userId));
                command.Parameters.AddWithValue("$base", baseRevision);
                command.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(settings, _jsonOptions));
                command.Parameters.AddWithValue("$now", Now());
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
                {
                    return new StoreResult(StoreOutcome.Updated, new KidProfile(userId, baseRevision + 1, settings));
                }

                var current = await ReadAsync(connection, transaction, userId, cancellationToken).ConfigureAwait(false);
                return new StoreResult(current is null ? StoreOutcome.NotFound : StoreOutcome.Conflict, current);
            },
            cancellationToken);
    }

    /// <summary>
    /// Unmarks a kid.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if a profile was removed.</returns>
    public async Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
        => await DeleteManyAsync([userId], cancellationToken).ConfigureAwait(false) == 1;

    /// <summary>
    /// Removes the profiles of the given users.
    /// </summary>
    /// <param name="userIds">The Jellyfin user ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many profiles were removed.</returns>
    public Task<int> DeleteManyAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        var keys = userIds.Select(Key).Distinct().ToList();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                var removed = 0;
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM kid_profile WHERE user_id = $id";
                var id = command.Parameters.Add("$id", SqliteType.Text);
                foreach (var key in keys)
                {
                    id.Value = key;
                    removed += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                return removed;
            },
            cancellationToken);
    }

    private static async Task<KidProfile?> ReadAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid userId, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision, settings FROM kid_profile WHERE user_id = $id";
        command.Parameters.AddWithValue("$id", Key(userId));
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var settings = JsonSerializer.Deserialize<KidProfileSettings>(reader.GetString(1), _jsonOptions) ?? new KidProfileSettings();
        return new KidProfile(userId, reader.GetInt64(0), settings);
    }

    private static string Key(Guid userId) => userId.ToString("N", CultureInfo.InvariantCulture);

    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private void EnsureUsable()
    {
        if (!_database.EnsureInitialized())
        {
            throw new InvalidOperationException("The kids database is not usable; see the server log.");
        }
    }
}
