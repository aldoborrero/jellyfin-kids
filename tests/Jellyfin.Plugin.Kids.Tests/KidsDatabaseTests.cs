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
        Assert.Equal(2L, ReadScalar<long>(path, "PRAGMA user_version"));
        Assert.Equal(2L, ReadScalar<long>(path, "SELECT count(*) FROM kid_profile"));
        Assert.Equal(3L, ReadScalar<long>(path, "SELECT revision FROM kid_profile WHERE user_id = 'a'"));
        Assert.Equal("{\"SearchAllowed\":true}", ReadScalar<string>(path, "SELECT settings FROM kid_profile WHERE user_id = 'b'"));
        Assert.Equal(0L, ReadScalar<long>(path, "SELECT count(*) FROM parent_lock"));
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
