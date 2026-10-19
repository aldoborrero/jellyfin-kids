using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// Reads and writes kids' screen time: daily usage, bonus minutes and reported offline segments (data-model.md).
/// </summary>
/// <remarks>
/// Usage only grows: the only write to <c>usage_day.used_ms</c> adds a positive amount. Rows are deleted only by
/// pruning, a purge or the user being deleted.
/// </remarks>
public sealed class UsageStore
{
    /// <summary>
    /// How many days of screen time are kept, and how old a reported segment may be.
    /// </summary>
    public const int RetentionDays = 90;

    private const string AddUsageSql = """
        INSERT INTO usage_day (user_id, day, used_ms) VALUES ($id, $day, $ms)
        ON CONFLICT (user_id, day) DO UPDATE SET used_ms = used_ms + excluded.used_ms
        """;

    private static readonly string[] _tables = ["usage_day", "bonus", "reported_segment"];

    private readonly KidsDatabase _database;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsageStore"/> class.
    /// </summary>
    /// <param name="database">The plugin database.</param>
    public UsageStore(KidsDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Adds playing time to kids' days, all in one transaction.
    /// </summary>
    /// <param name="amounts">Milliseconds to add per kid and day; every amount must be positive.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the amounts are stored.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An amount is zero or negative; nothing is written.</exception>
    public Task AddUsageAsync(IReadOnlyDictionary<(Guid UserId, string Day), long> amounts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        foreach (var amount in amounts)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount.Value, nameof(amounts));
        }

        EnsureUsable();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = AddUsageSql;
                var id = command.Parameters.Add("$id", SqliteType.Text);
                var day = command.Parameters.Add("$day", SqliteType.Text);
                var ms = command.Parameters.Add("$ms", SqliteType.Integer);
                foreach (var amount in amounts)
                {
                    id.Value = Key(amount.Key.UserId);
                    day.Value = amount.Key.Day;
                    ms.Value = amount.Value;
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Gets a kid's stored usage for a day.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="day">The day, <c>yyyy-MM-dd</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The milliseconds used; 0 when nothing is stored.</returns>
    public Task<long> GetUsedMsAsync(Guid userId, string day, CancellationToken cancellationToken = default)
        => ReadSumAsync("SELECT used_ms FROM usage_day WHERE user_id = $id AND day = $day", userId, day, cancellationToken);

    /// <summary>
    /// Adds bonus minutes to a kid's day.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="day">The day, <c>yyyy-MM-dd</c>.</param>
    /// <param name="minutes">The minutes, 1–240 (the table refuses others).</param>
    /// <param name="grantedBy">Who added them: a user name or "API key".</param>
    /// <param name="createdAt">When.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the bonus is stored.</returns>
    public Task AddBonusAsync(Guid userId, string day, int minutes, string grantedBy, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO bonus (user_id, day, minutes, granted_by, created_at) VALUES ($id, $day, $minutes, $by, $at)
                    """;
                command.Parameters.AddWithValue("$id", Key(userId));
                command.Parameters.AddWithValue("$day", day);
                command.Parameters.AddWithValue("$minutes", minutes);
                command.Parameters.AddWithValue("$by", grantedBy);
                command.Parameters.AddWithValue("$at", Timestamp(createdAt));
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <summary>
    /// Gets the sum of a kid's bonus minutes for a day.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="day">The day, <c>yyyy-MM-dd</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The minutes; 0 when there are none.</returns>
    public async Task<int> GetBonusMinutesAsync(Guid userId, string day, CancellationToken cancellationToken = default)
        => (int)await ReadSumAsync("SELECT sum(minutes) FROM bonus WHERE user_id = $id AND day = $day", userId, day, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Stores offline segments a kid reported, adding each new one's seconds to its day, in one transaction. A segment
    /// id the kid already reported adds nothing, so retries are safe.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="segments">The validated segments, each with the day it started in.</param>
    /// <param name="receivedAt">When the report arrived.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many segments were new.</returns>
    public Task<int> AddSegmentsAsync(
        Guid userId,
        IReadOnlyList<(string Id, string Day, DateTimeOffset StartedAt, int Seconds)> segments,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segments);
        foreach (var segment in segments)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segment.Seconds, nameof(segments));
        }

        EnsureUsable();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO reported_segment (user_id, segment_id, day, started_at, seconds, received_at)
                    VALUES ($id, $segment, $day, $started, $seconds, $received)
                    ON CONFLICT (user_id, segment_id) DO NOTHING
                    """;
                insert.Parameters.AddWithValue("$id", Key(userId));
                var segmentId = insert.Parameters.Add("$segment", SqliteType.Text);
                var day = insert.Parameters.Add("$day", SqliteType.Text);
                var started = insert.Parameters.Add("$started", SqliteType.Text);
                var seconds = insert.Parameters.Add("$seconds", SqliteType.Integer);
                insert.Parameters.AddWithValue("$received", Timestamp(receivedAt));

                using var add = connection.CreateCommand();
                add.Transaction = transaction;
                add.CommandText = AddUsageSql;
                add.Parameters.AddWithValue("$id", Key(userId));
                var addDay = add.Parameters.Add("$day", SqliteType.Text);
                var ms = add.Parameters.Add("$ms", SqliteType.Integer);

                var added = 0;
                foreach (var segment in segments)
                {
                    segmentId.Value = segment.Id;
                    day.Value = segment.Day;
                    started.Value = Timestamp(segment.StartedAt);
                    seconds.Value = segment.Seconds;
                    if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
                    {
                        continue;
                    }

                    addDay.Value = segment.Day;
                    ms.Value = segment.Seconds * 1000L;
                    await add.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    added++;
                }

                return added;
            },
            cancellationToken);
    }

    /// <summary>
    /// Deletes a user's usage, bonuses and reported segments.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    public Task<int> PurgeUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => DeleteAsync("user_id = $value", Key(userId), cancellationToken);

    /// <summary>
    /// Deletes the rows of every day before a cutoff.
    /// </summary>
    /// <param name="cutoffDay">The first day to keep, <c>yyyy-MM-dd</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    public Task<int> PruneAsync(string cutoffDay, CancellationToken cancellationToken = default)
        => DeleteAsync("day < $value", cutoffDay, cancellationToken);

    /// <summary>
    /// Deletes the rows of every user outside a set (users deleted while the plugin was not running).
    /// </summary>
    /// <param name="userIds">The users to keep.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Table names are constants; values are parameters")]
    public Task<int> DeleteUsersNotInAsync(IReadOnlySet<Guid> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        EnsureUsable();
        var keep = userIds.Select(Key).ToHashSet();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                var stored = new HashSet<string>();
                using (var select = connection.CreateCommand())
                {
                    select.Transaction = transaction;
                    select.CommandText = string.Join(" UNION ", _tables.Select(t => $"SELECT user_id FROM {t}"));
                    using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        stored.Add(reader.GetString(0));
                    }
                }

                var removed = 0;
                foreach (var user in stored.Where(u => !keep.Contains(u)))
                {
                    removed += await DeleteRowsAsync(connection, transaction, "user_id = $value", user, cancellationToken).ConfigureAwait(false);
                }

                return removed;
            },
            cancellationToken);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Table names and conditions are constants; values are parameters")]
    private static async Task<int> DeleteRowsAsync(SqliteConnection connection, SqliteTransaction transaction, string condition, string value, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var table in _tables)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table} WHERE {condition}";
            command.Parameters.AddWithValue("$value", value);
            removed += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    private static string Key(Guid userId) => userId.ToString("N", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset at) => at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private Task<int> DeleteAsync(string condition, string value, CancellationToken cancellationToken)
    {
        EnsureUsable();
        return _database.WriteAsync(
            (connection, transaction) => DeleteRowsAsync(connection, transaction, condition, value, cancellationToken),
            cancellationToken);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Callers pass constant SQL; values are parameters")]
    private async Task<long> ReadSumAsync(string sql, Guid userId, string day, CancellationToken cancellationToken)
    {
        EnsureUsable();
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", Key(userId));
        command.Parameters.AddWithValue("$day", day);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private void EnsureUsable()
    {
        if (!_database.EnsureInitialized())
        {
            throw new InvalidOperationException("The kids database is not usable; see the server log.");
        }
    }
}
