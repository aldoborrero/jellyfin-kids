using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// The plugin's SQLite database, <c>kids.db</c> in the kids data folder <c>&lt;data&gt;/kids</c>
/// (<see cref="IApplicationPaths.DataPath"/>, beside the server's own database; feature 14).
/// </summary>
/// <remarks>
/// Up to 0.0.9.0 the database lived in <c>BasePlugin.DataFolderPath</c> (<c>plugins/Jellyfin.Plugin.Kids</c>), which
/// looks like a stray plugin install; the first <see cref="EnsureInitialized"/> moves it with
/// <see cref="LegacyDataMigration"/> before opening anything. Nothing touches the disk before that call.
/// </remarks>
public sealed class KidsDatabase : IDisposable
{
    /// <summary>
    /// The database file name.
    /// </summary>
    public const string FileName = "kids.db";

    /// <summary>
    /// The kids data folder's name under <see cref="IApplicationPaths.DataPath"/>.
    /// </summary>
    public const string FolderName = "kids";

    /// <summary>
    /// The old data folder's name under <see cref="IApplicationPaths.PluginsPath"/> (the plugin's <c>DataFolderPath</c>).
    /// </summary>
    public const string OldFolderName = "Jellyfin.Plugin.Kids";

    /// <summary>
    /// The schema version this code writes (<c>PRAGMA user_version</c>).
    /// </summary>
    public const long SchemaVersion = 5;

    // Each step moves the schema from version (index) to version (index + 1)
    private static readonly string[] _migrations =
    [
        """
        CREATE TABLE kid_profile (
          user_id    TEXT    PRIMARY KEY,
          revision   INTEGER NOT NULL CHECK (revision >= 1),
          settings   TEXT    NOT NULL,
          created_at TEXT    NOT NULL,
          updated_at TEXT    NOT NULL
        );
        """,
        """
        CREATE TABLE parent_lock (
          id         INTEGER PRIMARY KEY CHECK (id = 1),
          lock_type  TEXT    NOT NULL CHECK (lock_type IN ('NUMERIC','REMOTE')),
          verifier   TEXT    NOT NULL,
          changed_at TEXT    NOT NULL
        );
        """,
        """
        CREATE TABLE usage_day (
          user_id TEXT NOT NULL, day TEXT NOT NULL, used_ms INTEGER NOT NULL CHECK (used_ms >= 0),
          PRIMARY KEY (user_id, day));
        CREATE TABLE bonus (
          id INTEGER PRIMARY KEY, user_id TEXT NOT NULL, day TEXT NOT NULL,
          minutes INTEGER NOT NULL CHECK (minutes BETWEEN 1 AND 240), granted_by TEXT NOT NULL, created_at TEXT NOT NULL);
        CREATE INDEX bonus_user_day ON bonus (user_id, day);
        CREATE TABLE reported_segment (
          user_id TEXT NOT NULL, segment_id TEXT NOT NULL, day TEXT NOT NULL, started_at TEXT NOT NULL,
          seconds INTEGER NOT NULL, received_at TEXT NOT NULL, PRIMARY KEY (user_id, segment_id));
        """,
        // Settings live in the JSON, so this step only gives every existing profile the new key, set to null
        """
        UPDATE kid_profile SET settings = json_insert(settings, '$.SessionLimitMinutes', NULL);
        """,
        // Bonus ids are part of the API from feature 13 (undo): AUTOINCREMENT so an undone or reset id is never reused
        """
        CREATE TABLE bonus_new (
          id INTEGER PRIMARY KEY AUTOINCREMENT, user_id TEXT NOT NULL, day TEXT NOT NULL,
          minutes INTEGER NOT NULL CHECK (minutes BETWEEN 1 AND 240), granted_by TEXT NOT NULL, created_at TEXT NOT NULL);
        INSERT INTO bonus_new (id, user_id, day, minutes, granted_by, created_at)
          SELECT id, user_id, day, minutes, granted_by, created_at FROM bonus;
        DROP TABLE bonus;
        ALTER TABLE bonus_new RENAME TO bonus;
        CREATE INDEX bonus_user_day ON bonus (user_id, day);
        """
    ];

    private readonly string? _oldFolder;
    private readonly ILogger<KidsDatabase> _logger;
    private readonly TimeProvider _time;
    private readonly Lazy<bool> _initialized;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsDatabase"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server paths.</param>
    /// <param name="logger">The logger.</param>
    public KidsDatabase(IApplicationPaths applicationPaths, ILogger<KidsDatabase> logger)
        : this(
            Path.Combine(applicationPaths.DataPath, FolderName),
            Path.Combine(applicationPaths.PluginsPath, OldFolderName),
            logger,
            TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsDatabase"/> class in a given folder, with no old folder.
    /// </summary>
    /// <param name="folder">The folder that holds the database file.</param>
    /// <param name="logger">The logger.</param>
    internal KidsDatabase(string folder, ILogger<KidsDatabase> logger)
        : this(folder, null, logger, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsDatabase"/> class in a given folder.
    /// </summary>
    /// <param name="folder">The folder that holds the database file.</param>
    /// <param name="oldFolder">The folder an older version kept the database in, or <c>null</c>.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="time">The clock, for the names of files the move renames.</param>
    internal KidsDatabase(string folder, string? oldFolder, ILogger<KidsDatabase> logger, TimeProvider time)
    {
        Folder = Path.GetFullPath(folder);
        _oldFolder = oldFolder;
        _logger = logger;
        _time = time;
        DatabasePath = Path.Combine(Folder, FileName);
        _initialized = new Lazy<bool>(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Gets the kids data folder (absolute).
    /// </summary>
    public string Folder { get; }

    /// <summary>
    /// Gets the path of the database file.
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Creates and migrates the database the first time it is called; later calls return the same result.
    /// Never throws.
    /// </summary>
    /// <returns><c>true</c> if the database is usable.</returns>
    public bool EnsureInitialized() => _initialized.Value;

    /// <summary>
    /// Opens a connection with the plugin's settings: private cache (shared cache is process-wide and would lock the
    /// server's own database), no pooling (the file is not held open between calls) and WAL.
    /// </summary>
    /// <returns>An open connection.</returns>
    internal SqliteConnection OpenConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs a write in its own transaction, one writer at a time.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="write">The write; the transaction is committed when it returns.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of <paramref name="write"/>.</returns>
    internal async Task<T> WriteAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> write, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = OpenConnection();
            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var result = await write(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _writeLock.Dispose();

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Migration SQL and versions are constants")]
    private bool Initialize()
    {
        // Before any connection: opening would create an empty database at the new place
        if (_oldFolder is not null
            && new LegacyDataMigration(_oldFolder, Folder, _logger, _time).Run() is MigrationOutcome.Refused or MigrationOutcome.Failed)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            DataFolderNotes.Write(Path.Combine(Folder, DataFolderNotes.FileName), DataFolderNotes.DataFolder(Folder), _logger);
            using var connection = OpenConnection();
            var version = ReadUserVersion(connection);
            if (version > SchemaVersion)
            {
                _logger.LogError(
                    "Kids database {Path} has schema version {Version}, newer than this plugin supports ({Supported}); kids data is unavailable",
                    DatabasePath,
                    version,
                    SchemaVersion);
                return false;
            }

            if (version < SchemaVersion)
            {
                using var transaction = connection.BeginTransaction();
                for (var step = version; step < SchemaVersion; step++)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = _migrations[step] + $"\nPRAGMA user_version = {step + 1};";
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
                _logger.LogInformation("Kids database {Path} migrated from schema version {From} to {To}", DatabasePath, version, SchemaVersion);
            }

            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Cannot open the kids database {Path}; kids data is unavailable", DatabasePath);
            return false;
        }
    }

    private static long ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return (long)command.ExecuteScalar()!;
    }
}
