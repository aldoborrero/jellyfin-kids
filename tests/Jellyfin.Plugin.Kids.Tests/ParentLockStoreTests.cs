using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.ParentLock;
using Jellyfin.Plugin.Kids.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class ParentLockStoreTests : IDisposable
{
    private static readonly DateTime _t0 = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _folder = new();
    private readonly KidsDatabase _database;
    private readonly ParentLockStore _store;

    public ParentLockStoreTests()
    {
        _database = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        _store = new ParentLockStore(_database);
    }

    public void Dispose()
    {
        _database.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task Get_WhenEmpty_IsNull()
    {
        Assert.Null(await _store.GetAsync());
    }

    [Fact]
    public async Task Upsert_SetsThenReplaces()
    {
        await _store.UpsertAsync(new StoredLock(ParentLockType.Numeric, "v1", _t0));
        await _store.UpsertAsync(new StoredLock(ParentLockType.Remote, "v2", _t0.AddMinutes(1)));

        var stored = await _store.GetAsync();

        Assert.Equal(new StoredLock(ParentLockType.Remote, "v2", _t0.AddMinutes(1)), stored);
        Assert.Equal(DateTimeKind.Utc, stored!.ChangedAt.Kind);
    }

    [Fact]
    public async Task InsertIfAbsent_RefusesWhenSet()
    {
        Assert.True(await _store.InsertIfAbsentAsync(new StoredLock(ParentLockType.Numeric, "v1", _t0)));
        Assert.False(await _store.InsertIfAbsentAsync(new StoredLock(ParentLockType.Remote, "v2", _t0)));

        Assert.Equal("v1", (await _store.GetAsync())!.Verifier);
    }

    [Fact]
    public async Task InsertIfAbsent_Concurrently_OneWins()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Task.Run(() => _store.InsertIfAbsentAsync(new StoredLock(ParentLockType.Numeric, $"v{i}", _t0)))));

        Assert.Single(results, r => r);
    }

    [Fact]
    public async Task ReplaceIfVerifierMatches_OnlyWhenUnchanged()
    {
        await _store.UpsertAsync(new StoredLock(ParentLockType.Numeric, "v1", _t0));

        Assert.False(await _store.ReplaceIfVerifierMatchesAsync("other", new StoredLock(ParentLockType.Remote, "v2", _t0)));
        Assert.True(await _store.ReplaceIfVerifierMatchesAsync("v1", new StoredLock(ParentLockType.Remote, "v2", _t0)));

        Assert.Equal("v2", (await _store.GetAsync())!.Verifier);
    }

    [Fact]
    public async Task Delete_RemovesOnce()
    {
        await _store.UpsertAsync(new StoredLock(ParentLockType.Numeric, "v1", _t0));

        Assert.True(await _store.DeleteAsync());
        Assert.False(await _store.DeleteAsync());
        Assert.Null(await _store.GetAsync());
    }

    [Theory]
    [InlineData("INSERT INTO parent_lock VALUES (2, 'NUMERIC', 'v', 'now')")]
    [InlineData("INSERT INTO parent_lock VALUES (1, 'CHALLENGE', 'v', 'now')")]
    public void Table_EnforcesOneRowAndKnownTypes(string sql)
    {
        Assert.True(_database.EnsureInitialized());
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }
}
