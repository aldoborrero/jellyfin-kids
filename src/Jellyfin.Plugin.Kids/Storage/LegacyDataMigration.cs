using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// Moves <c>kids.db</c> from the old folder (<c>plugins/Jellyfin.Plugin.Kids</c>, up to 0.0.9.0) to the kids data
/// folder, before anything opens it (feature 14, research R7–R11).
/// </summary>
/// <remarks>
/// The copy is SQLite's online backup of the old database (which includes its write-ahead log) into
/// <c>kids.db.migrating</c>; only a verified copy is renamed to <c>kids.db</c>, and only then is the old file renamed
/// <c>kids.db.migrated-&lt;date&gt;</c>. So a crash at any step leaves either the old database untouched or a verified
/// new one, and the next start resumes. Nothing is deleted but the unverified temporary copy.
/// </remarks>
internal sealed class LegacyDataMigration
{
    /// <summary>
    /// The suffix of the temporary copy.
    /// </summary>
    public const string TempSuffix = ".migrating";

    /// <summary>
    /// How much later one database must have been written to count as the newer one (FAT and SMB round to 2 s).
    /// </summary>
    public static readonly TimeSpan NewerBy = TimeSpan.FromSeconds(2);

    private static readonly string[] _sideFiles = ["-wal", "-shm"];

    private readonly string _oldFolder;
    private readonly string _folder;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyDataMigration"/> class.
    /// </summary>
    /// <param name="oldFolder">The old data folder.</param>
    /// <param name="folder">The kids data folder.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="time">The clock, for the names of renamed files.</param>
    public LegacyDataMigration(string oldFolder, string folder, ILogger logger, TimeProvider time)
    {
        _oldFolder = oldFolder;
        _folder = folder;
        _logger = logger;
        _time = time;
        OldPath = Path.Combine(oldFolder, KidsDatabase.FileName);
        NewPath = Path.Combine(folder, KidsDatabase.FileName);
        TempPath = NewPath + TempSuffix;
    }

    /// <summary>
    /// Gets the old database file.
    /// </summary>
    public string OldPath { get; }

    /// <summary>
    /// Gets the new database file.
    /// </summary>
    public string NewPath { get; }

    /// <summary>
    /// Gets the temporary copy.
    /// </summary>
    public string TempPath { get; }

    /// <summary>
    /// Gets or sets how long to wait for a database another program has locked, in seconds.
    /// </summary>
    internal int BusyTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Gets or sets a test hook called after each step (<c>copied</c>, <c>verified</c>, <c>switched</c>); throwing
    /// from it stands for a crash.
    /// </summary>
    internal Action<string>? AfterStep { get; set; }

    /// <summary>
    /// Runs the move. Never throws for file or database errors.
    /// </summary>
    /// <returns>What it did. <see cref="MigrationOutcome.Refused"/> and <see cref="MigrationOutcome.Failed"/> mean the
    /// kids data must not be used this run.</returns>
    public MigrationOutcome Run()
    {
        try
        {
            if (!File.Exists(OldPath))
            {
                DiscardTemp();
                return MigrationOutcome.NothingToDo;
            }

            return File.Exists(NewPath) ? ResolveBoth() : Move();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return Fail(ex.Message, ex);
        }
    }

    private MigrationOutcome Move()
    {
        _logger.LogInformation("Kids data found at the old location {OldPath}; moving it to {NewPath}", OldPath, NewPath);
        Directory.CreateDirectory(_folder);
        DiscardTemp();

        DatabaseSnapshot source;
        using (var from = Open(OldPath, SqliteOpenMode.ReadWrite))
        {
            source = DatabaseSnapshot.Read(from);
            if (source.IntegrityOk)
            {
                using var to = Open(TempPath, SqliteOpenMode.ReadWriteCreate);
                from.BackupDatabase(to);
            }
        }

        if (!source.IntegrityOk)
        {
            return Fail("its integrity check failed", null);
        }

        _logger.LogInformation("Copied {OldPath} to {TempPath} ({Rows} rows: {Tables})", OldPath, TempPath, source.TotalRows, source.DescribeRows());
        AfterStep?.Invoke("copied");

        DatabaseSnapshot copy;
        using (var check = Open(TempPath, SqliteOpenMode.ReadWrite))
        {
            copy = DatabaseSnapshot.Read(check);
        }

        if (!copy.IntegrityOk || !copy.SameDataAs(source) || HasSideFiles(TempPath))
        {
            return Fail("the copy did not match the old database", null);
        }

        _logger.LogInformation(
            "Verified the copy {TempPath}: integrity ok, schema version {Version}, same rows as {OldPath}",
            TempPath,
            copy.UserVersion,
            OldPath);
        AfterStep?.Invoke("verified");

        File.Move(TempPath, NewPath, overwrite: false);
        _logger.LogInformation("Kids data now in {NewPath}", NewPath);
        AfterStep?.Invoke("switched");

        RenameOld("migrated-" + Stamp("yyyyMMdd"));
        return MigrationOutcome.Migrated;
    }

    private MigrationOutcome ResolveBoth()
    {
        // Before opening either: closing a connection checkpoints its write-ahead log and moves the file's time
        var oldWrite = LastWrite(OldPath);
        var newWrite = LastWrite(NewPath);
        DatabaseSnapshot oldData, newData;
        using (var connection = Open(OldPath, SqliteOpenMode.ReadWrite))
        {
            oldData = DatabaseSnapshot.Read(connection);
        }

        using (var connection = Open(NewPath, SqliteOpenMode.ReadWrite))
        {
            newData = DatabaseSnapshot.Read(connection);
        }

        if (oldData.SameDataAs(newData))
        {
            _logger.LogInformation(
                "Kids data is in {NewPath}, and {OldPath} holds the same data: finishing an interrupted move",
                NewPath,
                OldPath);
            RenameOld("migrated-" + Stamp("yyyyMMdd"));
            return MigrationOutcome.Finished;
        }

        var newIsNewer = newWrite - oldWrite > NewerBy;
        var oldIsNewer = oldWrite - newWrite > NewerBy;
        if (newIsNewer && (newData.HasKidsData || !oldData.HasKidsData))
        {
            var renamed = RenameOld("older-" + Stamp("yyyyMMdd-HHmmss"));
            _logger.LogWarning(
                "Kids data is in both {NewPath} (last written {NewWrite:u}) and {OldPath} (last written {OldWrite:u}); using the newer {UsedPath} and keeping the other as {RenamedPath}",
                NewPath,
                newWrite,
                OldPath,
                oldWrite,
                NewPath,
                renamed);
            return MigrationOutcome.KeptNew;
        }

        if (oldIsNewer && (oldData.HasKidsData || !newData.HasKidsData))
        {
            var renamed = Rename(NewPath, "older-" + Stamp("yyyyMMdd-HHmmss"));
            _logger.LogWarning(
                "Kids data is in both {NewPath} (last written {NewWrite:u}) and {OldPath} (last written {OldWrite:u}); using the newer {UsedPath} and keeping the other as {RenamedPath}",
                NewPath,
                newWrite,
                OldPath,
                oldWrite,
                OldPath,
                renamed);
            return Move();
        }

        _logger.LogError(
            "Kids data is in both {NewPath} (last written {NewWrite:u}, {NewRows} rows) and {OldPath} (last written {OldWrite:u}, {OldRows} rows), and the plugin cannot tell which to keep; kids data is unavailable. Keep the one you want, move the other out of its folder, and restart Jellyfin",
            NewPath,
            newWrite,
            newData.TotalRows,
            OldPath,
            oldWrite,
            oldData.TotalRows);
        return MigrationOutcome.Refused;
    }

    private MigrationOutcome Fail(string reason, Exception? exception)
    {
        _logger.LogError(
            exception,
            "Cannot move the kids data from {OldPath} to {NewPath}: {Reason}; the old database is untouched and kids data is unavailable until this is fixed and Jellyfin restarts",
            OldPath,
            NewPath,
            reason);
        try
        {
            DeleteWithSideFiles(TempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Cannot delete the unfinished copy {TempPath}", TempPath);
        }

        return MigrationOutcome.Failed;
    }

    private string RenameOld(string suffix)
    {
        var renamed = Rename(OldPath, suffix);
        _logger.LogInformation("Renamed the old database {OldPath} to {RenamedPath}; it is kept as a backup for one release", OldPath, renamed);
        DataFolderNotes.Write(
            Path.Combine(_oldFolder, DataFolderNotes.FileName),
            DataFolderNotes.OldFolder(Path.GetFullPath(_folder), Path.GetFileName(renamed)),
            _logger);
        return renamed;
    }

    // kids.db → kids.db.<suffix>, with -wal/-shm following so SQLite still pairs them; never overwrites
    private static string Rename(string path, string suffix)
    {
        var target = path + "." + suffix;
        for (var n = 2; File.Exists(target) || HasSideFiles(target); n++)
        {
            target = path + "." + suffix + "-" + n.ToString(CultureInfo.InvariantCulture);
        }

        File.Move(path, target, overwrite: false);
        foreach (var side in _sideFiles)
        {
            if (File.Exists(path + side))
            {
                File.Move(path + side, target + side, overwrite: false);
            }
        }

        return target;
    }

    private void DiscardTemp()
    {
        if (File.Exists(TempPath) || HasSideFiles(TempPath) || File.Exists(TempPath + "-journal"))
        {
            DeleteWithSideFiles(TempPath);
            _logger.LogWarning("Discarded an unfinished copy {TempPath} from an earlier start", TempPath);
        }
    }

    private static void DeleteWithSideFiles(string path)
    {
        File.Delete(path);
        File.Delete(path + "-journal");
        foreach (var side in _sideFiles)
        {
            File.Delete(path + side);
        }
    }

    private static bool HasSideFiles(string path) => File.Exists(path + "-wal") || File.Exists(path + "-shm");

    private static DateTime LastWrite(string path)
    {
        var write = File.GetLastWriteTimeUtc(path);
        var wal = path + "-wal";
        return File.Exists(wal) && File.GetLastWriteTimeUtc(wal) > write ? File.GetLastWriteTimeUtc(wal) : write;
    }

    private string Stamp(string format) => _time.GetLocalNow().ToString(format, CultureInfo.InvariantCulture);

    // Same settings as KidsDatabase.OpenConnection, without changing the journal mode of the old file
    private SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = BusyTimeoutSeconds
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
