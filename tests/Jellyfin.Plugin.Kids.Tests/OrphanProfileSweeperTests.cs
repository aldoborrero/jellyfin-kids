using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Hosted;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class OrphanProfileSweeperTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly KidsDatabase _db;
    private readonly KidProfileStore _store;
    private readonly FakeUserDirectory _users = new();

    public OrphanProfileSweeperTests()
    {
        _db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        _store = new KidProfileStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task StartAsync_RemovesProfilesOfDeletedUsers()
    {
        var kept = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        await _store.InsertAsync(kept, new KidProfileSettings());
        await _store.InsertAsync(deleted, new KidProfileSettings());
        _users.Users.Add(kept);
        _users.Users.Add(Guid.NewGuid());

        await Sweeper(_db, _store).StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal([kept], (await _store.ListAsync()).Select(k => k.UserId));
    }

    [Fact]
    public async Task StartAsync_RemovesScreenTimeOfDeletedUsers()
    {
        var kept = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var usage = new UsageStore(_db);
        await usage.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long> { [(kept, "2026-10-18")] = 1000, [(deleted, "2026-10-18")] = 1000 });
        _users.Users.Add(kept);

        await Sweeper(_db, _store).StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1000, await usage.GetUsedMsAsync(kept, "2026-10-18"));
        Assert.Equal(0, await usage.GetUsedMsAsync(deleted, "2026-10-18"));
    }

    [Fact]
    public async Task StartAsync_WithNoUsers_RemovesNothing()
    {
        var kid = Guid.NewGuid();
        await _store.InsertAsync(kid, new KidProfileSettings());

        await Sweeper(_db, _store).StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal([kid], (await _store.ListAsync()).Select(k => k.UserId));
    }

    [Fact]
    public async Task StartAsync_InitializesTheDatabase()
    {
        _users.Users.Add(Guid.NewGuid());

        await Sweeper(_db, _store).StartAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(_db.DatabasePath));
    }

    [Fact]
    public async Task StartAsync_WithUnusableDatabase_DoesNotThrow()
    {
        Directory.CreateDirectory(_folder.Path);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_folder.Path, KidsDatabase.FileName)};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99";
            command.ExecuteNonQuery();
        }

        using var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        _users.Users.Add(Guid.NewGuid());

        await Sweeper(db, new KidProfileStore(db)).StartAsync(TestContext.Current.CancellationToken);

        Assert.False(db.EnsureInitialized());
    }

    private OrphanProfileSweeper Sweeper(KidsDatabase db, KidProfileStore store)
        => new(db, store, new UsageStore(db), _users, NullLogger<OrphanProfileSweeper>.Instance);
}
