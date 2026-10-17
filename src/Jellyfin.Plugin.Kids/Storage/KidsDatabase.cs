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
/// The plugin's SQLite database, <c>kids.db</c> in the plugin data folder.
/// </summary>
/// <remarks>
/// Plugin services are registered before the plugin instance exists, so the folder comes from
/// <see cref="IApplicationPaths.PluginsPath"/> (the same folder as <c>BasePlugin.DataFolderPath</c>), and nothing
/// touches the disk until the first <see cref="EnsureInitialized"/>.
/// </remarks>
public sealed class KidsDatabase : IDisposable
{
    /// <summary>
    /// The database file name.
    /// </summary>
    public const string FileName = "kids.db";

    /// <summary>
    /// The schema version this code writes (<c>PRAGMA user_version</c>).
    /// </summary>
    public const long SchemaVersion = 1;

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
        """
    ];

    private readonly string _folder;
    private readonly ILogger<KidsDatabase> _logger;
    private readonly Lazy<bool> _initialized;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsDatabase"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server paths.</param>
    /// <param name="logger">The logger.</param>
    public KidsDatabase(IApplicationPaths applicationPaths, ILogger<KidsDatabase> logger)
        : this(Path.Combine(applicationPaths.PluginsPath, "Jellyfin.Plugin.Kids"), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsDatabase"/> class in a given folder.
    /// </summary>
    /// <param name="folder">The folder that holds the database file.</param>
    /// <param name="logger">The logger.</param>
    internal KidsDatabase(string folder, ILogger<KidsDatabase> logger)
    {
        _folder = folder;
        _logger = logger;
        DatabasePath = Path.Combine(folder, FileName);
        _initialized = new Lazy<bool>(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);
    }

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
        try
        {
            Directory.CreateDirectory(_folder);
            using var connection = OpenConnection();
            var version = ReadUserVersion(connection);
            if (version > SchemaVersion)
            {
                _logger.LogError(
                    "Kids database {Path} has schema version {Version}, newer than this plugin supports ({Supported}); kid profiles are unavailable",
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
            _logger.LogError(ex, "Cannot open the kids database {Path}; kid profiles are unavailable", DatabasePath);
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
