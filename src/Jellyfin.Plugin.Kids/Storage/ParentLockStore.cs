using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.ParentLock;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// Reads and writes the household parent lock: at most one row, enforced by the table.
/// </summary>
public sealed class ParentLockStore
{
    private readonly KidsDatabase _database;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParentLockStore"/> class.
    /// </summary>
    /// <param name="database">The plugin database.</param>
    public ParentLockStore(KidsDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Gets the household lock.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lock, or null if none is set.</returns>
    public async Task<StoredLock?> GetAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        using var connection = _database.OpenConnection();
        return await ReadAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the lock, replacing any existing one.
    /// </summary>
    /// <param name="value">The new lock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the lock is stored.</returns>
    public Task UpsertAsync(StoredLock value, CancellationToken cancellationToken = default)
        => WriteAsync(
            """
            INSERT INTO parent_lock (id, lock_type, verifier, changed_at) VALUES (1, $type, $verifier, $changed)
            ON CONFLICT (id) DO UPDATE SET lock_type = $type, verifier = $verifier, changed_at = $changed
            """,
            value,
            null,
            cancellationToken);

    /// <summary>
    /// Sets the lock only if none is set, atomically.
    /// </summary>
    /// <param name="value">The new lock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the lock was stored; <c>false</c> if one was already set.</returns>
    public async Task<bool> InsertIfAbsentAsync(StoredLock value, CancellationToken cancellationToken = default)
        => await WriteAsync(
            """
            INSERT INTO parent_lock (id, lock_type, verifier, changed_at) VALUES (1, $type, $verifier, $changed)
            ON CONFLICT (id) DO NOTHING
            """,
            value,
            null,
            cancellationToken).ConfigureAwait(false) == 1;

    /// <summary>
    /// Replaces the lock only if its verifier is still <paramref name="expectedVerifier"/>, so a change made between
    /// checking the current code and writing is never overwritten.
    /// </summary>
    /// <param name="expectedVerifier">The verifier the caller's current code was checked against.</param>
    /// <param name="value">The new lock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the lock was replaced.</returns>
    public async Task<bool> ReplaceIfVerifierMatchesAsync(string expectedVerifier, StoredLock value, CancellationToken cancellationToken = default)
        => await WriteAsync(
            "UPDATE parent_lock SET lock_type = $type, verifier = $verifier, changed_at = $changed WHERE id = 1 AND verifier = $expected",
            value,
            expectedVerifier,
            cancellationToken).ConfigureAwait(false) == 1;

    /// <summary>
    /// Removes the lock.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if a lock was removed.</returns>
    public Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM parent_lock";
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
            },
            cancellationToken);
    }

    private static async Task<StoredLock?> ReadAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT lock_type, verifier, changed_at FROM parent_lock WHERE id = 1";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var changedAt = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return new StoredLock(reader.GetString(0), reader.GetString(1), changedAt);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Callers pass constant SQL; values are parameters")]
    private Task<int> WriteAsync(string sql, StoredLock value, string? expectedVerifier, CancellationToken cancellationToken)
    {
        EnsureUsable();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$type", value.LockType);
                command.Parameters.AddWithValue("$verifier", value.Verifier);
                command.Parameters.AddWithValue("$changed", value.ChangedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                if (expectedVerifier is not null)
                {
                    command.Parameters.AddWithValue("$expected", expectedVerifier);
                }

                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private void EnsureUsable()
    {
        if (!_database.EnsureInitialized())
        {
            throw new InvalidOperationException("The kids database is not usable; see the server log.");
        }
    }
}
