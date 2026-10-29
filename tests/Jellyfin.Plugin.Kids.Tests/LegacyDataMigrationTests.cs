using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.ParentLock;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

/// <summary>
/// The move of kids.db from plugins/Jellyfin.Plugin.Kids to data/kids (feature 14), on real files.
/// </summary>
public sealed class LegacyDataMigrationTests : IDisposable
{
    private static readonly Guid _kid = Guid.ParseExact("0123456789abcdef0123456789abcdef", "N");
    private static readonly Guid _otherKid = Guid.ParseExact("fedcba9876543210fedcba9876543210", "N");
    private static readonly DateTime _t0 = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _root = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ListLogger<KidsDatabase> _log = new();

    public LegacyDataMigrationTests()
    {
        OldFolder = Path.Combine(_root.Path, "plugins", "Jellyfin.Plugin.Kids");
        NewFolder = Path.Combine(_root.Path, "data", "kids");
    }

    private string OldFolder { get; }

    private string NewFolder { get; }

    private string OldDb => Path.Combine(OldFolder, KidsDatabase.FileName);

    private string NewDb => Path.Combine(NewFolder, KidsDatabase.FileName);

    private string Today => _time.GetLocalNow().ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose() => _root.Dispose();

    [Fact]
    public void FreshInstall_NothingToDo_AndKidsDatabaseCreatesOnlyTheNewFile()
    {
        Assert.Equal(MigrationOutcome.NothingToDo, Migration().Run());

        using var db = Database();
        Assert.True(db.EnsureInitialized());

        Assert.True(File.Exists(NewDb));
        Assert.False(Directory.Exists(OldFolder));
        Assert.True(File.Exists(Path.Combine(NewFolder, DataFolderNotes.FileName)));
    }

    [Fact]
    public async Task OldOnly_IsCopiedVerifiedSwitchedAndRenamed()
    {
        await SeedAsync(OldFolder, _kid, "verifier-a", bonuses: 3);

        var migration = Migration();
        Assert.Equal(MigrationOutcome.Migrated, migration.Run());

        Assert.False(File.Exists(OldDb));
        Assert.False(File.Exists(migration.TempPath));
        var renamed = Path.Combine(OldFolder, "kids.db.migrated-" + Today);
        Assert.True(File.Exists(renamed));
        await AssertSeededAsync(NewFolder, _kid, "verifier-a", bonuses: 3);
        Assert.Equal(Snapshot(renamed).Fingerprint, Snapshot(NewDb).Fingerprint);

        var oldNote = File.ReadAllText(Path.Combine(OldFolder, DataFolderNotes.FileName));
        Assert.Contains(Path.GetFullPath(NewFolder), oldNote, StringComparison.Ordinal);
        Assert.Contains("kids.db.migrated-" + Today, oldNote, StringComparison.Ordinal);

        // Every step is logged, in order
        var info = _log.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Message).ToList();
        Assert.Collection(
            info,
            m => Assert.StartsWith("Kids data found at the old location", m, StringComparison.Ordinal),
            m => Assert.Matches(@"^Copied .* \(\d+ rows: .*kid_profile=1.*\)$", m),
            m => Assert.Matches("^Verified the copy .*: integrity ok, schema version 5, same rows as ", m),
            m => Assert.StartsWith("Kids data now in", m, StringComparison.Ordinal),
            m => Assert.StartsWith("Renamed the old database", m, StringComparison.Ordinal));
    }

    [Fact]
    public async Task OldOnly_ThroughKidsDatabase_ServesTheMovedData()
    {
        await SeedAsync(OldFolder, _kid, "verifier-a", bonuses: 2);

        using var db = Database();
        Assert.True(db.EnsureInitialized());

        Assert.Equal(Path.Combine(Path.GetFullPath(NewFolder), KidsDatabase.FileName), db.DatabasePath);
        Assert.NotNull(await new KidProfileStore(db).GetAsync(_kid));
        Assert.Equal("verifier-a", (await new ParentLockStore(db).GetAsync())!.Verifier);
        Assert.False(File.Exists(OldDb));
    }

    [Fact]
    public async Task NewOnly_NothingToDo_AndASecondRunChangesNothing()
    {
        await SeedAsync(OldFolder, _kid, "verifier-a");
        Assert.Equal(MigrationOutcome.Migrated, Migration().Run());
        var before = Listing();

        Assert.Equal(MigrationOutcome.NothingToDo, Migration().Run());
        Assert.Equal(before, Listing());
    }

    [Fact]
    public async Task UnmergedWal_RowsOnlyInTheWalArriveInTheNewDatabase()
    {
        // A server that stopped without checkpointing: kids.db plus a -wal holding the last writes
        var live = Path.Combine(_root.Path, "live");
        await SeedAsync(live, _kid, "verifier-a");
        using (var writer = new SqliteConnection($"Data Source={Path.Combine(live, KidsDatabase.FileName)};Pooling=False"))
        {
            writer.Open();
            Execute(writer, "PRAGMA wal_autocheckpoint = 0");
            Execute(writer, $"INSERT INTO usage_day VALUES ('{_otherKid:N}', '2026-10-18', 4242)");
            Directory.CreateDirectory(OldFolder);
            foreach (var name in new[] { "kids.db", "kids.db-wal", "kids.db-shm" })
            {
                File.Copy(Path.Combine(live, name), Path.Combine(OldFolder, name));
            }
        }

        Assert.True(new FileInfo(OldDb + "-wal").Length > 0);
        var mainOnly = Path.Combine(_root.Path, "main-only.db");
        File.Copy(OldDb, mainOnly);
        Assert.Equal(0L, Scalar<long>(mainOnly, $"SELECT count(*) FROM usage_day WHERE user_id = '{_otherKid:N}'"));

        Assert.Equal(MigrationOutcome.Migrated, Migration().Run());

        Assert.Equal(4242L, Scalar<long>(NewDb, $"SELECT used_ms FROM usage_day WHERE user_id = '{_otherKid:N}'"));
        Assert.False(File.Exists(OldDb + "-wal"));
    }

    [Fact]
    public async Task MigratedNameTaken_GetsANumberAndOverwritesNothing()
    {
        Directory.CreateDirectory(OldFolder);
        var taken = Path.Combine(OldFolder, "kids.db.migrated-" + Today);
        File.WriteAllText(taken, "an earlier backup");
        await SeedAsync(OldFolder, _kid, "verifier-a");

        Assert.Equal(MigrationOutcome.Migrated, Migration().Run());

        Assert.Equal("an earlier backup", File.ReadAllText(taken));
        Assert.True(File.Exists(taken + "-2"));
    }

    [Fact]
    public async Task CrashMidCopy_PartialTempFile_IsDiscardedAndTheMoveRedone()
    {
        await SeedAsync(OldFolder, _kid, "verifier-a");
        var original = File.ReadAllBytes(OldDb);
        Directory.CreateDirectory(NewFolder);
        var migration = Migration();
        File.WriteAllBytes(migration.TempPath, original.Take(original.Length / 2).ToArray());

        // Nothing opens the half copy as the database
        Assert.False(File.Exists(NewDb));
        Assert.Equal(MigrationOutcome.Migrated, migration.Run());

        await AssertSeededAsync(NewFolder, _kid, "verifier-a");
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.StartsWith("Discarded an unfinished copy", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("copied")]
    [InlineData("verified")]
    public async Task CrashBeforeTheSwitch_LeavesTheOldDatabaseInUseAndTheNextStartMoves(string step)
    {
        await SeedAsync(OldFolder, _kid, "verifier-a");
        var original = File.ReadAllBytes(OldDb);
        var crashing = Migration();
        crashing.AfterStep = s => CrashAt(s, step);

        Assert.Throws<InvalidOperationException>(() => crashing.Run());

        Assert.False(File.Exists(NewDb));
        Assert.Equal(original, File.ReadAllBytes(OldDb));
        Assert.True(File.Exists(crashing.TempPath));

        Assert.Equal(MigrationOutcome.Migrated, Migration().Run());
        await AssertSeededAsync(NewFolder, _kid, "verifier-a");
        Assert.False(File.Exists(crashing.TempPath));
    }

    [Fact]
    public async Task CrashAfterTheSwitch_BothHoldTheSameData_TheNextStartFinishes()
    {
        await SeedAsync(OldFolder, _kid, "verifier-a");
        var crashing = Migration();
        crashing.AfterStep = s => CrashAt(s, "switched");
        Assert.Throws<InvalidOperationException>(() => crashing.Run());
        Assert.True(File.Exists(OldDb));
        Assert.True(File.Exists(NewDb));

        Assert.Equal(MigrationOutcome.Finished, Migration().Run());

        Assert.False(File.Exists(OldDb));
        Assert.True(File.Exists(Path.Combine(OldFolder, "kids.db.migrated-" + Today)));
        await AssertSeededAsync(NewFolder, _kid, "verifier-a");
    }

    [Fact]
    public async Task Both_NewWrittenLater_KeepsNewAndRenamesOldAside()
    {
        await SeedAsync(OldFolder, _kid, "verifier-old");
        await SeedAsync(NewFolder, _otherKid, "verifier-new");
        File.SetLastWriteTimeUtc(OldDb, _t0);
        File.SetLastWriteTimeUtc(NewDb, _t0.AddMinutes(5));

        Assert.Equal(MigrationOutcome.KeptNew, Migration().Run());

        Assert.False(File.Exists(OldDb));
        var aside = Assert.Single(Directory.GetFiles(OldFolder, "kids.db.older-*"));
        Assert.Equal(1L, Scalar<long>(aside, $"SELECT count(*) FROM kid_profile WHERE user_id = '{_kid:N}'"));
        await AssertSeededAsync(NewFolder, _otherKid, "verifier-new");
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("using the newer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Both_OldWrittenLater_MovesOldAndKeepsTheNewOneAside()
    {
        await SeedAsync(OldFolder, _kid, "verifier-old");
        await SeedAsync(NewFolder, _otherKid, "verifier-new");
        File.SetLastWriteTimeUtc(NewDb, _t0);
        File.SetLastWriteTimeUtc(OldDb, _t0.AddMinutes(5));

        Assert.Equal(MigrationOutcome.Migrated, Migration().Run());

        await AssertSeededAsync(NewFolder, _kid, "verifier-old");
        var aside = Assert.Single(Directory.GetFiles(NewFolder, "kids.db.older-*"));
        Assert.Equal("verifier-new", Scalar<string>(aside, "SELECT verifier FROM parent_lock"));
        Assert.True(File.Exists(Path.Combine(OldFolder, "kids.db.migrated-" + Today)));
    }

    [Fact]
    public async Task Both_WrittenWithinTwoSeconds_IsRefusedAndTouchesNothing()
    {
        await SeedAsync(OldFolder, _kid, "verifier-old");
        await SeedAsync(NewFolder, _otherKid, "verifier-new");
        File.SetLastWriteTimeUtc(OldDb, _t0);
        File.SetLastWriteTimeUtc(NewDb, _t0.AddSeconds(1));
        var before = Contents();

        Assert.Equal(MigrationOutcome.Refused, Migration().Run());

        Assert.Equal(before, Contents());
        var error = Assert.Single(_log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("cannot tell which to keep", error.Message, StringComparison.Ordinal);
        Assert.Contains(OldDb, error.Message, StringComparison.Ordinal);
        Assert.Contains(NewDb, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_NewerIsEmptyButOlderHasKids_IsRefused()
    {
        // The usual downgrade story: 0.0.9.0 made a fresh database at the old place after the move
        await SeedAsync(NewFolder, _kid, "verifier-new");
        using (var downgraded = new KidsDatabase(OldFolder, NullLogger<KidsDatabase>.Instance))
        {
            Assert.True(downgraded.EnsureInitialized());
        }

        File.SetLastWriteTimeUtc(NewDb, _t0);
        File.SetLastWriteTimeUtc(OldDb, _t0.AddDays(1));
        var before = Contents();

        Assert.Equal(MigrationOutcome.Refused, Migration().Run());
        Assert.Equal(before, Contents());

        using var db = Database();
        Assert.False(db.EnsureInitialized());
        Assert.Equal(before, Contents());
    }

    [Fact]
    public async Task LockedOldDatabase_Fails_LeavesItUntouchedAndCreatesNothing()
    {
        await SeedAsync(OldFolder, _kid, "verifier-a");
        var original = File.ReadAllBytes(OldDb);

        using (var holder = new SqliteConnection($"Data Source={OldDb};Pooling=False"))
        {
            holder.Open();
            Execute(holder, "PRAGMA locking_mode = EXCLUSIVE");
            Execute(holder, "BEGIN EXCLUSIVE");

            var migration = Migration();
            migration.BusyTimeoutSeconds = 1;
            Assert.Equal(MigrationOutcome.Failed, migration.Run());

            Assert.False(File.Exists(NewDb));
            Assert.False(File.Exists(migration.TempPath));
            Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("Cannot move the kids data", StringComparison.Ordinal));
            Execute(holder, "ROLLBACK");
        }

        Assert.Equal(original, File.ReadAllBytes(OldDb));
    }

    [Fact]
    public void GarbageOldDatabase_Fails_AndKidsDatabaseStaysUnavailable()
    {
        Directory.CreateDirectory(OldFolder);
        var garbage = Enumerable.Range(0, 8192).Select(i => (byte)(i * 7)).ToArray();
        File.WriteAllBytes(OldDb, garbage);

        using var db = Database();
        Assert.False(db.EnsureInitialized());

        Assert.Equal(garbage, File.ReadAllBytes(OldDb));
        Assert.False(File.Exists(NewDb));
        Assert.False(File.Exists(NewDb + LegacyDataMigration.TempSuffix));
    }

    [Fact]
    public async Task DamagedOldDatabasePage_Fails_AndIsLeftAsItWas()
    {
        await SeedAsync(OldFolder, _kid, "verifier-a", bonuses: 400);
        var bytes = File.ReadAllBytes(OldDb);
        var pageSize = (bytes[16] << 8) | bytes[17];
        Assert.True(bytes.Length > pageSize * 4);

        // Scribble over the header and cell pointers of a b-tree page
        for (var i = pageSize * 3; i < (pageSize * 3) + 300; i++)
        {
            bytes[i] = 0x5A;
        }

        File.WriteAllBytes(OldDb, bytes);

        Assert.Equal(MigrationOutcome.Failed, Migration().Run());

        Assert.Equal(bytes, File.ReadAllBytes(OldDb));
        Assert.False(File.Exists(NewDb));
        Assert.False(File.Exists(NewDb + LegacyDataMigration.TempSuffix));
    }

    [Fact]
    public async Task OldDatabaseThatReadsButFailsIntegrityCheck_Fails_AndIsLeftAsItWas()
    {
        await SeedAsync(OldFolder, _kid, "verifier-a");
        var bytes = File.ReadAllBytes(OldDb);

        // A wrong freelist page count in the header: every query still works, integrity_check does not answer "ok"
        bytes[39] = 7;
        File.WriteAllBytes(OldDb, bytes);
        Assert.Equal(1L, Scalar<long>(OldDb, "SELECT count(*) FROM kid_profile"));
        Assert.NotEqual("ok", Scalar<string>(OldDb, "PRAGMA integrity_check"));
        bytes = File.ReadAllBytes(OldDb);

        Assert.Equal(MigrationOutcome.Failed, Migration().Run());

        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("its integrity check failed", StringComparison.Ordinal));
        Assert.Equal(bytes, File.ReadAllBytes(OldDb));
        Assert.False(File.Exists(NewDb));
        Assert.False(File.Exists(NewDb + LegacyDataMigration.TempSuffix));
    }

    [Fact]
    public async Task StrayTempWithNoOldDatabase_IsDiscarded_AndTheStartIsFresh()
    {
        await SeedAsync(OldFolder, _kid, "verifier-a");
        Directory.CreateDirectory(NewFolder);
        var migration = Migration();
        File.Move(OldDb, migration.TempPath);

        Assert.Equal(MigrationOutcome.NothingToDo, migration.Run());

        Assert.False(File.Exists(migration.TempPath));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void DataFolderNote_NamesTheFolderAndIsRewrittenWhenChanged()
    {
        using (var db = Database())
        {
            Assert.True(db.EnsureInitialized());
        }

        var note = Path.Combine(NewFolder, DataFolderNotes.FileName);
        var text = File.ReadAllText(note);
        Assert.Contains("DO NOT DELETE THIS FOLDER", text, StringComparison.Ordinal);
        Assert.Contains("Folder: " + Path.GetFullPath(NewFolder), text, StringComparison.Ordinal);

        File.WriteAllText(note, "edited");
        using (var again = Database())
        {
            Assert.True(again.EnsureInitialized());
        }

        Assert.Equal(text, File.ReadAllText(note));
    }

    // Throwing from the step hook stands for the process dying there
    private static void CrashAt(string step, string crashStep)
    {
        if (step == crashStep)
        {
            throw new InvalidOperationException("power cut");
        }
    }

    private LegacyDataMigration Migration() => new(OldFolder, NewFolder, _log, _time);

    private KidsDatabase Database() => new(NewFolder, OldFolder, _log, _time);

    // A database written the way the plugin writes it: a kid, the lock, usage and bonuses
    private static async Task SeedAsync(string folder, Guid kid, string verifier, int bonuses = 1)
    {
        using var db = new KidsDatabase(folder, NullLogger<KidsDatabase>.Instance);
        await new KidProfileStore(db).InsertAsync(kid, new KidProfileSettings { AutoplayCap = 4, DailyAllowanceMinutes = 45 });
        await new ParentLockStore(db).UpsertAsync(new StoredLock(ParentLockType.Numeric, verifier, _t0));
        var usage = new UsageStore(db);
        await usage.AddUsageAsync(new System.Collections.Generic.Dictionary<(Guid, string), long> { [(kid, "2026-10-18")] = 1234 });
        for (var i = 0; i < bonuses; i++)
        {
            await usage.AddBonusAsync(kid, "2026-10-18", 5, "admin", new DateTimeOffset(_t0));
        }
    }

    private static async Task AssertSeededAsync(string folder, Guid kid, string verifier, int bonuses = 1)
    {
        using var db = new KidsDatabase(folder, NullLogger<KidsDatabase>.Instance);
        var profile = await new KidProfileStore(db).GetAsync(kid);
        Assert.NotNull(profile);
        Assert.Equal(4, profile.Settings.AutoplayCap);
        Assert.Equal(45, profile.Settings.DailyAllowanceMinutes);
        Assert.Equal(verifier, (await new ParentLockStore(db).GetAsync())!.Verifier);
        var usage = new UsageStore(db);
        Assert.Equal(1234L, await usage.GetUsedMsAsync(kid, "2026-10-18"));
        Assert.Equal(bonuses, (await usage.ListBonusesAsync(kid, "2026-10-18")).Count);
    }

    private static DatabaseSnapshot Snapshot(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return DatabaseSnapshot.Read(connection);
    }

    private string Listing()
        => string.Join('\n', Directory.GetFiles(_root.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => f + " " + new FileInfo(f).Length + " " + File.GetLastWriteTimeUtc(f).Ticks));

    private string Contents()
        => string.Join('\n', Directory.GetFiles(_root.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => f + " " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))));

    private static T Scalar<T>(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)command.ExecuteScalar()!;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
