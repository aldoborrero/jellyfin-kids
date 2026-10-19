using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class KidProfileStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly KidsDatabase _db;
    private readonly KidProfileStore _store;

    public KidProfileStoreTests()
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
    public async Task Insert_CreatesAtRevisionOne()
    {
        var userId = Guid.NewGuid();
        var settings = new KidProfileSettings { SearchAllowed = true };

        var result = await _store.InsertAsync(userId, settings);

        Assert.Equal(StoreOutcome.Created, result.Outcome);
        Assert.Equal(new KidProfile(userId, 1, settings), result.Profile, ProfileComparer.Instance);
        Assert.Equal(result.Profile, await _store.GetAsync(userId), ProfileComparer.Instance);
    }

    [Fact]
    public async Task Insert_ExistingUser_ReportsAlreadyExistsWithCurrent()
    {
        var userId = Guid.NewGuid();
        await _store.InsertAsync(userId, new KidProfileSettings { AutoplayCap = 5 });

        var result = await _store.InsertAsync(userId, new KidProfileSettings());

        Assert.Equal(StoreOutcome.AlreadyExists, result.Outcome);
        Assert.Equal(5, result.Profile!.Settings.AutoplayCap);
    }

    [Fact]
    public async Task Insert_Concurrently_ExactlyOneIsCreated()
    {
        var userId = Guid.NewGuid();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(i => Task.Run(() => _store.InsertAsync(userId, new KidProfileSettings { AutoplayCap = i + 1 }))));

        var created = Assert.Single(results, r => r.Outcome == StoreOutcome.Created);
        var other = Assert.Single(results, r => r.Outcome == StoreOutcome.AlreadyExists);
        Assert.Equal(created.Profile, other.Profile, ProfileComparer.Instance);
    }

    [Fact]
    public async Task UpdateIfRevision_Current_IncrementsAndStores()
    {
        var userId = Guid.NewGuid();
        await _store.InsertAsync(userId, new KidProfileSettings());
        var settings = new KidProfileSettings
        {
            AutoplayEnabled = true,
            AutoplayCap = 2,
            Sections = HomeSectionEntry.Normalize([new(HomeSection.Picked, true), new(HomeSection.New, false)]),
            PickedItemIds = [Guid.NewGuid(), Guid.NewGuid()]
        };

        var result = await _store.UpdateIfRevisionAsync(userId, 1, settings);

        Assert.Equal(StoreOutcome.Updated, result.Outcome);
        Assert.Equal(new KidProfile(userId, 2, settings), await _store.GetAsync(userId), ProfileComparer.Instance);
    }

    [Fact]
    public async Task UpdateIfRevision_Stale_ReportsConflictAndKeepsRow()
    {
        var userId = Guid.NewGuid();
        await _store.InsertAsync(userId, new KidProfileSettings());
        await _store.UpdateIfRevisionAsync(userId, 1, new KidProfileSettings { SearchAllowed = true });

        var result = await _store.UpdateIfRevisionAsync(userId, 1, new KidProfileSettings { AutoplayCap = 9 });

        Assert.Equal(StoreOutcome.Conflict, result.Outcome);
        Assert.Equal(2, result.Profile!.Revision);
        Assert.True(result.Profile.Settings.SearchAllowed);
        Assert.Equal(KidProfileSettings.DefaultAutoplayCap, (await _store.GetAsync(userId))!.Settings.AutoplayCap);
    }

    [Fact]
    public async Task UpdateIfRevision_Missing_ReportsNotFound()
    {
        var result = await _store.UpdateIfRevisionAsync(Guid.NewGuid(), 1, new KidProfileSettings());

        Assert.Equal(StoreOutcome.NotFound, result.Outcome);
        Assert.Null(result.Profile);
    }

    [Fact]
    public async Task UpdateIfRevision_ConcurrentSameBase_ExactlyOneSucceeds()
    {
        var userId = Guid.NewGuid();
        await _store.InsertAsync(userId, new KidProfileSettings());

        var results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(i => Task.Run(() => _store.UpdateIfRevisionAsync(userId, 1, new KidProfileSettings { AutoplayCap = i + 1 }))));

        Assert.Single(results, r => r.Outcome == StoreOutcome.Updated);
        Assert.Equal(3, results.Count(r => r.Outcome == StoreOutcome.Conflict));
        Assert.Equal(2, (await _store.GetAsync(userId))!.Revision);
    }

    [Fact]
    public async Task Delete_RemovesOnce()
    {
        var userId = Guid.NewGuid();
        await _store.InsertAsync(userId, new KidProfileSettings());

        Assert.True(await _store.DeleteAsync(userId));
        Assert.False(await _store.DeleteAsync(userId));
        Assert.Null(await _store.GetAsync(userId));
    }

    [Fact]
    public async Task DeleteMany_RemovesOnlyGivenUsers()
    {
        var keep = Guid.NewGuid();
        var drop1 = Guid.NewGuid();
        var drop2 = Guid.NewGuid();
        foreach (var id in new[] { keep, drop1, drop2 })
        {
            await _store.InsertAsync(id, new KidProfileSettings());
        }

        Assert.Equal(2, await _store.DeleteManyAsync([drop1, drop2, Guid.NewGuid()]));
        Assert.Equal([keep], (await _store.ListAsync()).Select(s => s.UserId));
    }

    [Fact]
    public async Task List_ReturnsUserIdsAndRevisions()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await _store.InsertAsync(a, new KidProfileSettings());
        await _store.InsertAsync(b, new KidProfileSettings());
        await _store.UpdateIfRevisionAsync(b, 1, new KidProfileSettings());

        var list = await _store.ListAsync();

        Assert.Equal(
            new[] { new KidSummary(a, 1), new KidSummary(b, 2) }.OrderBy(s => s.UserId),
            list.OrderBy(s => s.UserId));
    }

    [Fact]
    public async Task Get_SettingsJsonWithUnknownAndMissingProperties_UsesDefaults()
    {
        var userId = Guid.NewGuid();
        Assert.True(_db.EnsureInitialized());
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_folder.Path, KidsDatabase.FileName)};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO kid_profile VALUES ($id, 4, $s, '2026-10-17T00:00:00Z', '2026-10-17T00:00:00Z')";
            command.Parameters.AddWithValue("$id", userId.ToString("N"));
            command.Parameters.AddWithValue("$s", """{"SearchAllowed":true,"FutureField":42}""");
            command.ExecuteNonQuery();
        }

        var profile = await _store.GetAsync(userId);

        Assert.Equal(new KidProfile(userId, 4, new KidProfileSettings { SearchAllowed = true }), profile, ProfileComparer.Instance);
    }

    [Fact]
    public async Task Get_SettingsStoredBeforeTheAllowance_ReadsAsUnlimited()
    {
        var userId = Guid.NewGuid();
        Assert.True(_db.EnsureInitialized());
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_folder.Path, KidsDatabase.FileName)};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO kid_profile VALUES ($id, 2, $s, '2026-10-17T00:00:00Z', '2026-10-17T00:00:00Z')";
            command.Parameters.AddWithValue("$id", userId.ToString("N"));
            command.Parameters.AddWithValue("$s", """{"SearchAllowed":false,"AutoplayEnabled":true,"AutoplayCap":3,"PickedItemIds":[]}""");
            command.ExecuteNonQuery();
        }

        var profile = await _store.GetAsync(userId);

        Assert.Null(profile!.Settings.DailyAllowanceMinutes);
    }

    [Fact]
    public async Task Insert_KeepsTheAllowance()
    {
        var userId = Guid.NewGuid();
        await _store.InsertAsync(userId, new KidProfileSettings { DailyAllowanceMinutes = 45 });

        Assert.Equal(45, (await _store.GetAsync(userId))!.Settings.DailyAllowanceMinutes);
    }

    [Fact]
    public async Task Get_SettingsStoredWithoutTheSessionLimit_ReadsAsNone()
    {
        var userId = Guid.NewGuid();
        Assert.True(_db.EnsureInitialized());
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_folder.Path, KidsDatabase.FileName)};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO kid_profile VALUES ($id, 2, $s, '2026-10-18T00:00:00Z', '2026-10-18T00:00:00Z')";
            command.Parameters.AddWithValue("$id", userId.ToString("N"));
            command.Parameters.AddWithValue("$s", """{"AutoplayCap":3,"DailyAllowanceMinutes":45}""");
            command.ExecuteNonQuery();
        }

        var profile = await _store.GetAsync(userId);

        Assert.Null(profile!.Settings.SessionLimitMinutes);
        Assert.Equal(45, profile.Settings.DailyAllowanceMinutes);
    }

    [Fact]
    public async Task InsertAndUpdate_KeepTheSessionLimit()
    {
        var userId = Guid.NewGuid();
        await _store.InsertAsync(userId, new KidProfileSettings { SessionLimitMinutes = 30 });
        Assert.Equal(30, (await _store.GetAsync(userId))!.Settings.SessionLimitMinutes);

        var updated = await _store.UpdateIfRevisionAsync(userId, 1, new KidProfileSettings { SessionLimitMinutes = 120, DailyAllowanceMinutes = 60 });

        Assert.Equal(StoreOutcome.Updated, updated.Outcome);
        var stored = (await _store.GetAsync(userId))!.Settings;
        Assert.Equal(120, stored.SessionLimitMinutes);
        Assert.Equal(60, stored.DailyAllowanceMinutes);

        await _store.UpdateIfRevisionAsync(userId, 2, new KidProfileSettings());

        Assert.Null((await _store.GetAsync(userId))!.Settings.SessionLimitMinutes);
    }
}
