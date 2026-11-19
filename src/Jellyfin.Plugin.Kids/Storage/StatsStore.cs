using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Stats;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// Reads kids' statistics and writes the watch log (feature 22): daily usage, bonus and allowance per day, watch
/// segments and breaks.
/// </summary>
/// <remarks>
/// Pruning, purges and orphan sweeps of these tables go through <see cref="UsageStore"/>, which lists them.
/// </remarks>
public sealed class StatsStore
{
    private const string UpsertSegmentSql = """
        INSERT INTO watch_segment (id, playback_id, user_id, day, started_at, ended_at, seconds, item_id, item_type, item_name,
          series_id, series_name, season, episode, channel_id, channel_name, device, client)
        VALUES ($id, $playback, $user, $day, $started, $ended, $seconds, $item, $type, $name,
          $series, $seriesName, $season, $episode, $channel, $channelName, $device, $client)
        ON CONFLICT (id) DO UPDATE SET ended_at = excluded.ended_at, seconds = excluded.seconds
        """;

    private const string UpsertBreakSql = """
        INSERT INTO break_event (user_id, day, started_at, minutes, ended_by_grownup) VALUES ($user, $day, $started, $minutes, $grownup)
        ON CONFLICT (user_id, started_at) DO UPDATE SET minutes = excluded.minutes, ended_by_grownup = excluded.ended_by_grownup
        """;

    private const string SegmentColumns = """
        id, playback_id, user_id, day, started_at, ended_at, seconds, item_id, item_type, item_name,
        series_id, series_name, season, episode, channel_id, channel_name, device, client
        """;

    private readonly KidsDatabase _database;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatsStore"/> class.
    /// </summary>
    /// <param name="database">The plugin database.</param>
    public StatsStore(KidsDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Writes watch segments and breaks in one transaction: a segment already stored gets its new end and seconds, a
    /// break its new length.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <param name="breaks">The breaks.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when they are stored.</returns>
    public Task WriteAsync(IReadOnlyList<WatchSegment> segments, IReadOnlyList<BreakRecord> breaks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(breaks);
        EnsureUsable();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                foreach (var segment in segments)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = UpsertSegmentSql;
                    var item = segment.Item;
                    command.Parameters.AddWithValue("$id", segment.Id);
                    command.Parameters.AddWithValue("$playback", segment.PlaybackId);
                    command.Parameters.AddWithValue("$user", Key(segment.UserId));
                    command.Parameters.AddWithValue("$day", segment.Day);
                    command.Parameters.AddWithValue("$started", Timestamp(segment.StartedAt));
                    command.Parameters.AddWithValue("$ended", Timestamp(segment.EndedAt));
                    command.Parameters.AddWithValue("$seconds", segment.Seconds);
                    command.Parameters.AddWithValue("$item", Value(item.ItemId));
                    command.Parameters.AddWithValue("$type", Value(item.ItemType));
                    command.Parameters.AddWithValue("$name", Value(item.ItemName));
                    command.Parameters.AddWithValue("$series", Value(item.SeriesId));
                    command.Parameters.AddWithValue("$seriesName", Value(item.SeriesName));
                    command.Parameters.AddWithValue("$season", (object?)item.Season ?? DBNull.Value);
                    command.Parameters.AddWithValue("$episode", (object?)item.Episode ?? DBNull.Value);
                    command.Parameters.AddWithValue("$channel", Value(item.ChannelId));
                    command.Parameters.AddWithValue("$channelName", Value(item.ChannelName));
                    command.Parameters.AddWithValue("$device", Value(segment.Device));
                    command.Parameters.AddWithValue("$client", Value(segment.Client));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                foreach (var record in breaks)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = UpsertBreakSql;
                    command.Parameters.AddWithValue("$user", Key(record.UserId));
                    command.Parameters.AddWithValue("$day", record.Day);
                    command.Parameters.AddWithValue("$started", Timestamp(record.StartedAt));
                    command.Parameters.AddWithValue("$minutes", record.Minutes);
                    command.Parameters.AddWithValue("$grownup", record.EndedByGrownUp ? 1 : 0);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Gets a kid's stored days in a range: used time, bonus and the allowance noted for the day.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="fromDay">The first day, <c>yyyy-MM-dd</c>.</param>
    /// <param name="toDay">The last day, included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The days that have any row; days with nothing stored are missing.</returns>
    public async Task<IReadOnlyDictionary<string, StoredDay>> ReadDaysAsync(Guid userId, string fromDay, string toDay, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        var days = new Dictionary<string, StoredDay>(StringComparer.Ordinal);
        using var connection = _database.OpenConnection();
        await ReadAsync(
            connection,
            "SELECT day, used_ms FROM usage_day WHERE user_id = $id AND day BETWEEN $from AND $to",
            userId,
            fromDay,
            toDay,
            reader =>
            {
                var day = reader.GetString(0);
                days[day] = Get(days, day) with { UsedMs = reader.GetInt64(1) };
            },
            cancellationToken).ConfigureAwait(false);
        await ReadAsync(
            connection,
            "SELECT day, sum(minutes) FROM bonus WHERE user_id = $id AND day BETWEEN $from AND $to GROUP BY day",
            userId,
            fromDay,
            toDay,
            reader =>
            {
                var day = reader.GetString(0);
                days[day] = Get(days, day) with { BonusMinutes = reader.GetInt32(1) };
            },
            cancellationToken).ConfigureAwait(false);
        await ReadAsync(
            connection,
            "SELECT day, minutes FROM day_allowance WHERE user_id = $id AND day BETWEEN $from AND $to",
            userId,
            fromDay,
            toDay,
            reader =>
            {
                var day = reader.GetString(0);
                days[day] = Get(days, day) with { AllowanceKnown = true, AllowanceMinutes = reader.IsDBNull(1) ? null : reader.GetInt32(1) };
            },
            cancellationToken).ConfigureAwait(false);
        return days;
    }

    /// <summary>
    /// Gets a kid's watch segments in a range of days, oldest first.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="fromDay">The first day, <c>yyyy-MM-dd</c>.</param>
    /// <param name="toDay">The last day, included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The segments.</returns>
    public async Task<IReadOnlyList<WatchSegment>> ReadSegmentsAsync(Guid userId, string fromDay, string toDay, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        var segments = new List<WatchSegment>();
        using var connection = _database.OpenConnection();
        await ReadAsync(
            connection,
            "SELECT " + SegmentColumns + " FROM watch_segment WHERE user_id = $id AND day BETWEEN $from AND $to ORDER BY started_at",
            userId,
            fromDay,
            toDay,
            reader => segments.Add(new WatchSegment(
                reader.GetString(0),
                reader.GetString(1),
                Guid.ParseExact(reader.GetString(2), "N"),
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4)),
                ParseTimestamp(reader.GetString(5)),
                reader.GetInt32(6),
                new WatchItem(
                    GuidOrNull(reader, 7),
                    StringOrNull(reader, 8),
                    StringOrNull(reader, 9),
                    GuidOrNull(reader, 10),
                    StringOrNull(reader, 11),
                    reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    GuidOrNull(reader, 14),
                    StringOrNull(reader, 15)),
                StringOrNull(reader, 16),
                StringOrNull(reader, 17))),
            cancellationToken).ConfigureAwait(false);
        return segments;
    }

    /// <summary>
    /// Gets a kid's breaks in a range of days, oldest first.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="fromDay">The first day, <c>yyyy-MM-dd</c>.</param>
    /// <param name="toDay">The last day, included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The breaks.</returns>
    public async Task<IReadOnlyList<BreakRecord>> ReadBreaksAsync(Guid userId, string fromDay, string toDay, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        var breaks = new List<BreakRecord>();
        using var connection = _database.OpenConnection();
        await ReadAsync(
            connection,
            "SELECT user_id, day, started_at, minutes, ended_by_grownup FROM break_event WHERE user_id = $id AND day BETWEEN $from AND $to ORDER BY started_at",
            userId,
            fromDay,
            toDay,
            reader => breaks.Add(new BreakRecord(
                Guid.ParseExact(reader.GetString(0), "N"),
                reader.GetString(1),
                ParseTimestamp(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetInt32(4) == 1)),
            cancellationToken).ConfigureAwait(false);
        return breaks;
    }

    /// <summary>
    /// Counts the rows of the watch log, all kids together, and finds its oldest day.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The segments and breaks stored, and the oldest day or null when there are none.</returns>
    public async Task<(long Segments, long Breaks, string? OldestDay)> CountWatchLogAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT count(*) FROM watch_segment), (SELECT count(*) FROM break_event),
                   (SELECT min(day) FROM (SELECT day FROM watch_segment UNION ALL SELECT day FROM break_event))
            """;
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var oldest = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2);
        return (reader.GetInt64(0), reader.GetInt64(1), oldest);
    }

    /// <summary>
    /// Deletes the whole watch log: every segment and break of every kid. Daily usage, bonuses and allowances stay.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    public Task<int> DeleteWatchLogAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        return _database.WriteAsync(
            async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM watch_segment; DELETE FROM break_event;";
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private static StoredDay Get(Dictionary<string, StoredDay> days, string day)
        => days.TryGetValue(day, out var found) ? found : new StoredDay(0, 0, false, null);

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Callers pass constant SQL; values are parameters")]
    private static async Task ReadAsync(SqliteConnection connection, string sql, Guid userId, string fromDay, string toDay, Action<SqliteDataReader> row, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", Key(userId));
        command.Parameters.AddWithValue("$from", fromDay);
        command.Parameters.AddWithValue("$to", toDay);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            row(reader);
        }
    }

    private static string Key(Guid userId) => userId.ToString("N", CultureInfo.InvariantCulture);

    private static object Value(Guid? id) => id is { } value ? Key(value) : DBNull.Value;

    private static object Value(string? text) => (object?)text ?? DBNull.Value;

    private static Guid? GuidOrNull(SqliteDataReader reader, int column)
        => reader.IsDBNull(column) ? null : Guid.ParseExact(reader.GetString(column), "N");

    private static string? StringOrNull(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);

    private static string Timestamp(DateTimeOffset at) => at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string text)
        => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private void EnsureUsable()
    {
        if (!_database.EnsureInitialized())
        {
            throw new InvalidOperationException("The kids database is not usable; see the server log.");
        }
    }
}
