using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// What a kids database holds, read to verify a copy and to compare two databases (feature 14, research R8).
/// </summary>
/// <param name="IntegrityOk">Whether <c>PRAGMA integrity_check</c> answered exactly one row <c>ok</c>.</param>
/// <param name="UserVersion">The schema version (<c>PRAGMA user_version</c>).</param>
/// <param name="RowCounts">Rows per table, by table name.</param>
/// <param name="Fingerprint">SHA-256 over every table name and every row, each value tagged with its type, as hex.</param>
/// <param name="HasKidsData">Whether there is at least one kid profile or a parent lock.</param>
internal sealed record DatabaseSnapshot(
    bool IntegrityOk,
    long UserVersion,
    IReadOnlyDictionary<string, long> RowCounts,
    string Fingerprint,
    bool HasKidsData)
{
    /// <summary>
    /// Gets the total number of rows.
    /// </summary>
    public long TotalRows => RowCounts.Values.Sum();

    /// <summary>
    /// Whether two databases hold the same data: same schema version and the same rows in every table.
    /// </summary>
    /// <param name="other">The other snapshot.</param>
    /// <returns><c>true</c> if the data is the same.</returns>
    public bool SameDataAs(DatabaseSnapshot other)
        => other is not null && UserVersion == other.UserVersion && string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal);

    /// <summary>
    /// The row counts for a log line, as <c>table=rows, …</c>.
    /// </summary>
    /// <returns>The text.</returns>
    public string DescribeRows()
        => string.Join(", ", RowCounts.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.Key}={t.Value}")));

    /// <summary>
    /// Reads a snapshot from an open connection. Throws <see cref="SqliteException"/> when the file is not a readable
    /// database.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <returns>The snapshot.</returns>
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Table names come from sqlite_master and are quoted")]
    public static DatabaseSnapshot Read(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var integrity = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                integrity.Add(reader.GetString(0));
            }
        }

        long version;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version";
            version = (long)command.ExecuteScalar()!;
        }

        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var table in tables)
        {
            Append(hash, "table", table);
            long rows = 0;
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\" ORDER BY rowid";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows++;
                Append(hash, "row", string.Empty);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    AppendValue(hash, reader, i);
                }
            }

            counts[table] = rows;
        }

        var hasKidsData = counts.GetValueOrDefault("kid_profile") > 0 || counts.GetValueOrDefault("parent_lock") > 0;
        return new DatabaseSnapshot(
            integrity.Count == 1 && string.Equals(integrity[0], "ok", StringComparison.Ordinal),
            version,
            counts,
            Convert.ToHexString(hash.GetHashAndReset()),
            hasKidsData);
    }

    private static void AppendValue(IncrementalHash hash, SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            Append(hash, "null", string.Empty);
            return;
        }

        switch (reader.GetFieldType(ordinal))
        {
            case var t when t == typeof(long):
                Append(hash, "integer", reader.GetInt64(ordinal).ToString(CultureInfo.InvariantCulture));
                break;
            case var t when t == typeof(double):
                Append(hash, "real", reader.GetDouble(ordinal).ToString("R", CultureInfo.InvariantCulture));
                break;
            case var t when t == typeof(byte[]):
                Append(hash, "blob", Convert.ToHexString((byte[])reader.GetValue(ordinal)));
                break;
            default:
                Append(hash, "text", reader.GetString(ordinal));
                break;
        }
    }

    // Length-prefixed, so no two different sequences of values hash the same
    private static void Append(IncrementalHash hash, string tag, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(tag + ":" + value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}
