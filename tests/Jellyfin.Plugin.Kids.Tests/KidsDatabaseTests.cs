using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class KidsDatabaseTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Constructor_TouchesNothing()
    {
        _ = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);

        Assert.False(Directory.Exists(_folder.Path));
    }

    [Fact]
    public void EnsureInitialized_CreatesSchemaAtLatestVersion()
    {
        var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);

        Assert.True(db.EnsureInitialized());

        Assert.True(File.Exists(db.DatabasePath));
        Assert.Equal(KidsDatabase.SchemaVersion, ReadScalar<long>(db.DatabasePath, "PRAGMA user_version"));
        Assert.Equal(1L, ReadScalar<long>(db.DatabasePath, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'kid_profile'"));
    }

    [Fact]
    public void EnsureInitialized_OnExistingDatabase_IsNoOp()
    {
        Assert.True(new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance).EnsureInitialized());

        var again = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);

        Assert.True(again.EnsureInitialized());
        Assert.Equal(KidsDatabase.SchemaVersion, ReadScalar<long>(again.DatabasePath, "PRAGMA user_version"));
    }

    [Fact]
    public async Task EnsureInitialized_Concurrently_MigratesOnce()
    {
        var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(db.EnsureInitialized)));

        Assert.All(results, Assert.True);
        Assert.Equal(KidsDatabase.SchemaVersion, ReadScalar<long>(db.DatabasePath, "PRAGMA user_version"));
    }

    [Fact]
    public void EnsureInitialized_WithNewerSchema_IsUnusableAndLeavesFileAlone()
    {
        Directory.CreateDirectory(_folder.Path);
        var path = Path.Combine(_folder.Path, KidsDatabase.FileName);
        Execute(path, "PRAGMA user_version = 99");

        var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);

        Assert.False(db.EnsureInitialized());
        Assert.Equal(99L, ReadScalar<long>(path, "PRAGMA user_version"));
        Assert.Equal(0L, ReadScalar<long>(path, "SELECT count(*) FROM sqlite_master WHERE name = 'kid_profile'"));
    }

    [Fact]
    public void EnsureInitialized_FromVersion1_AddsParentLockAndKeepsProfiles()
    {
        Directory.CreateDirectory(_folder.Path);
        var path = Path.Combine(_folder.Path, KidsDatabase.FileName);
        Execute(path, """
            CREATE TABLE kid_profile (
              user_id    TEXT    PRIMARY KEY,
              revision   INTEGER NOT NULL CHECK (revision >= 1),
              settings   TEXT    NOT NULL,
              created_at TEXT    NOT NULL,
              updated_at TEXT    NOT NULL
            );
            INSERT INTO kid_profile VALUES ('a', 3, '{}', 'x', 'x'), ('b', 1, '{"SearchAllowed":true}', 'x', 'x');
            PRAGMA user_version = 1;
            """);

        var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);

        Assert.True(db.EnsureInitialized());
        Assert.Equal(KidsDatabase.SchemaVersion, ReadScalar<long>(path, "PRAGMA user_version"));
        Assert.Equal(2L, ReadScalar<long>(path, "SELECT count(*) FROM kid_profile"));
        Assert.Equal(3L, ReadScalar<long>(path, "SELECT revision FROM kid_profile WHERE user_id = 'a'"));
        Assert.Equal("{\"SearchAllowed\":true,\"SessionLimitMinutes\":null}", ReadScalar<string>(path, "SELECT settings FROM kid_profile WHERE user_id = 'b'"));
        Assert.Equal(0L, ReadScalar<long>(path, "SELECT count(*) FROM parent_lock"));
    }

    [Fact]
    public void EnsureInitialized_FromVersion2_AddsUsageTablesAndKeepsProfilesAndLock()
    {
        Directory.CreateDirectory(_folder.Path);
        var path = Path.Combine(_folder.Path, KidsDatabase.FileName);
        Execute(path, """
            CREATE TABLE kid_profile (
              user_id    TEXT    PRIMARY KEY,
              revision   INTEGER NOT NULL CHECK (revision >= 1),
              settings   TEXT    NOT NULL,
              created_at TEXT    NOT NULL,
              updated_at TEXT    NOT NULL
            );
            CREATE TABLE parent_lock (
              id         INTEGER PRIMARY KEY CHECK (id = 1),
              lock_type  TEXT    NOT NULL CHECK (lock_type IN ('NUMERIC','REMOTE')),
              verifier   TEXT    NOT NULL,
              changed_at TEXT    NOT NULL
            );
            INSERT INTO kid_profile VALUES ('a', 3, '{}', 'x', 'x'), ('b', 1, '{"SearchAllowed":true}', 'x', 'x');
            INSERT INTO parent_lock VALUES (1, 'NUMERIC', 'v', 'x');
            PRAGMA user_version = 2;
            """);

        var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);

        Assert.True(db.EnsureInitialized());
        Assert.Equal(KidsDatabase.SchemaVersion, ReadScalar<long>(path, "PRAGMA user_version"));
        Assert.Equal(3L, ReadScalar<long>(path, "SELECT revision FROM kid_profile WHERE user_id = 'a'"));
        Assert.Equal("{\"SearchAllowed\":true,\"SessionLimitMinutes\":null}", ReadScalar<string>(path, "SELECT settings FROM kid_profile WHERE user_id = 'b'"));
        Assert.Equal("v", ReadScalar<string>(path, "SELECT verifier FROM parent_lock"));
        Assert.Equal(3L, ReadScalar<long>(path, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('usage_day', 'bonus', 'reported_segment')"));
    }

    [Fact]
    public async Task EnsureInitialized_FromVersion3_AddsNullSessionLimitAndKeepsEverythingElse()
    {
        Directory.CreateDirectory(_folder.Path);
        var path = Path.Combine(_folder.Path, KidsDatabase.FileName);
        Execute(path, """
            CREATE TABLE kid_profile (
              user_id    TEXT    PRIMARY KEY,
              revision   INTEGER NOT NULL CHECK (revision >= 1),
              settings   TEXT    NOT NULL,
              created_at TEXT    NOT NULL,
              updated_at TEXT    NOT NULL
            );
            CREATE TABLE parent_lock (
              id         INTEGER PRIMARY KEY CHECK (id = 1),
              lock_type  TEXT    NOT NULL CHECK (lock_type IN ('NUMERIC','REMOTE')),
              verifier   TEXT    NOT NULL,
              changed_at TEXT    NOT NULL
            );
            CREATE TABLE usage_day (
              user_id TEXT NOT NULL, day TEXT NOT NULL, used_ms INTEGER NOT NULL CHECK (used_ms >= 0),
              PRIMARY KEY (user_id, day));
            CREATE TABLE bonus (
              id INTEGER PRIMARY KEY, user_id TEXT NOT NULL, day TEXT NOT NULL,
              minutes INTEGER NOT NULL CHECK (minutes BETWEEN 1 AND 240), granted_by TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE reported_segment (
              user_id TEXT NOT NULL, segment_id TEXT NOT NULL, day TEXT NOT NULL, started_at TEXT NOT NULL,
              seconds INTEGER NOT NULL, received_at TEXT NOT NULL, PRIMARY KEY (user_id, segment_id));
            INSERT INTO kid_profile VALUES
              ('0123456789abcdef0123456789abcdef', 4,
               '{"SearchAllowed":true,"AutoplayEnabled":true,"AutoplayCap":7,"PickedItemIds":[],"DailyAllowanceMinutes":45}', 'x', 'x'),
              ('b', 1, '{}', 'x', 'x'),
              ('c', 2, '{"SessionLimitMinutes":30}', 'x', 'x');
            INSERT INTO parent_lock VALUES (1, 'NUMERIC', 'v', 'x');
            INSERT INTO usage_day VALUES ('b', '2026-10-18', 1234);
            INSERT INTO bonus VALUES (1, 'b', '2026-10-18', 5, 'admin', 'x');
            PRAGMA user_version = 3;
            """);

        var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);

        Assert.True(db.EnsureInitialized());
        Assert.Equal(KidsDatabase.SchemaVersion, ReadScalar<long>(path, "PRAGMA user_version"));
        Assert.Equal("{\"SessionLimitMinutes\":null}", ReadScalar<string>(path, "SELECT settings FROM kid_profile WHERE user_id = 'b'"));
        Assert.Equal("{\"SessionLimitMinutes\":30}", ReadScalar<string>(path, "SELECT settings FROM kid_profile WHERE user_id = 'c'"));
        Assert.Equal("v", ReadScalar<string>(path, "SELECT verifier FROM parent_lock"));
        Assert.Equal(1234L, ReadScalar<long>(path, "SELECT used_ms FROM usage_day"));
        Assert.Equal(5L, ReadScalar<long>(path, "SELECT minutes FROM bonus"));

        // The store reads the migrated row with its settings intact and no session limit
        var profile = await new KidProfileStore(db).GetAsync(Guid.ParseExact("0123456789abcdef0123456789abcdef", "N"));
        Assert.NotNull(profile);
        Assert.Equal(4L, profile.Revision);
        Assert.True(profile.Settings.SearchAllowed);
        Assert.True(profile.Settings.AutoplayEnabled);
        Assert.Equal(7, profile.Settings.AutoplayCap);
        Assert.Equal(45, profile.Settings.DailyAllowanceMinutes);
        Assert.Null(profile.Settings.SessionLimitMinutes);
    }

    [Fact]
    public async Task EnsureInitialized_FromVersion4_RebuildsBonusWithAutoincrementAndKeepsIds()
    {
        Directory.CreateDirectory(_folder.Path);
        var path = Path.Combine(_folder.Path, KidsDatabase.FileName);
        Execute(path, """
            CREATE TABLE kid_profile (
              user_id TEXT PRIMARY KEY, revision INTEGER NOT NULL CHECK (revision >= 1), settings TEXT NOT NULL,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE parent_lock (
              id INTEGER PRIMARY KEY CHECK (id = 1), lock_type TEXT NOT NULL CHECK (lock_type IN ('NUMERIC','REMOTE')),
              verifier TEXT NOT NULL, changed_at TEXT NOT NULL);
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
            INSERT INTO usage_day VALUES ('0123456789abcdef0123456789abcdef', '2026-10-25', 1234);
            INSERT INTO bonus VALUES (3, '0123456789abcdef0123456789abcdef', '2026-10-25', 5, 'admin', '2026-10-25T10:00:00.0000000Z');
            INSERT INTO bonus VALUES (7, '0123456789abcdef0123456789abcdef', '2026-10-25', 20, 'mum', '2026-10-25T11:00:00.0000000Z');
            PRAGMA user_version = 4;
            """);

        var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);

        Assert.True(db.EnsureInitialized());
        Assert.Equal(5L, ReadScalar<long>(path, "PRAGMA user_version"));
        Assert.Contains("AUTOINCREMENT", ReadScalar<string>(path, "SELECT sql FROM sqlite_master WHERE name = 'bonus'"), StringComparison.Ordinal);
        Assert.Equal(1L, ReadScalar<long>(path, "SELECT count(*) FROM sqlite_master WHERE type = 'index' AND name = 'bonus_user_day'"));
        Assert.Equal(1234L, ReadScalar<long>(path, "SELECT used_ms FROM usage_day"));

        var store = new UsageStore(db);
        var kid = Guid.ParseExact("0123456789abcdef0123456789abcdef", "N");
        var bonuses = await store.ListBonusesAsync(kid, "2026-10-25");
        Assert.Equal([7L, 3L], bonuses.Select(b => b.Id));
        Assert.Equal("mum", bonuses[0].GrantedBy);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.Zero), bonuses[0].CreatedAt);

        // The newest id is not handed out again once it is deleted
        Assert.True(await store.DeleteBonusAsync(kid, 7));
        Assert.Equal(8L, await store.AddBonusAsync(kid, "2026-10-25", 5, "admin", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void OpenConnection_UsesPrivateCacheNoPoolingAndWal()
    {
        var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        Assert.True(db.EnsureInitialized());

        using var connection = db.OpenConnection();
        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString);

        Assert.Equal(SqliteCacheMode.Private, builder.Cache);
        Assert.False(builder.Pooling);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", command.ExecuteScalar() as string);
    }

    private static T ReadScalar<T>(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)command.ExecuteScalar()!;
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
