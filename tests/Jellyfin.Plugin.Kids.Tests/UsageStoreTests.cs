using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class UsageStoreTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _folder = new();
    private readonly KidsDatabase _db;
    private readonly UsageStore _store;
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    public UsageStoreTests()
    {
        _db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        _store = new UsageStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task AddUsage_InsertsThenAdds()
    {
        await _store.AddUsageAsync(Amounts((_alice, "2026-10-18", 1500), (_bob, "2026-10-18", 700)));
        await _store.AddUsageAsync(Amounts((_alice, "2026-10-18", 500)));

        Assert.Equal(2000, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
        Assert.Equal(700, await _store.GetUsedMsAsync(_bob, "2026-10-18"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AddUsage_NonPositiveDelta_ThrowsAndWritesNothing(long delta)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _store.AddUsageAsync(Amounts((_alice, "2026-10-18", 1000), (_bob, "2026-10-18", delta))));

        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
        Assert.Equal(0, await _store.GetUsedMsAsync(_bob, "2026-10-18"));
    }

    [Fact]
    public async Task AddUsage_IsOneTransaction()
    {
        // The second row fails inside the write, so the first must be rolled back too
        Execute($"CREATE TRIGGER fail_bob BEFORE INSERT ON usage_day WHEN NEW.user_id = '{_bob:N}' BEGIN SELECT RAISE(ABORT, 'test'); END");

        await Assert.ThrowsAnyAsync<Exception>(
            () => _store.AddUsageAsync(Amounts((_alice, "2026-10-18", 1000), (_bob, "2026-10-18", 1))));

        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
    }

    [Fact]
    public async Task GetUsed_MissingRow_IsZero()
    {
        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
    }

    [Fact]
    public void Table_RefusesNegativeUsage()
    {
        Assert.True(_db.EnsureInitialized());

        Assert.Throws<SqliteException>(
            () => Execute("INSERT INTO usage_day (user_id, day, used_ms) VALUES ('x', '2026-10-18', -1)"));
    }

    [Fact]
    public async Task Bonus_SumsTheDay()
    {
        await _store.AddBonusAsync(_alice, "2026-10-18", 15, "admin", _now);
        await _store.AddBonusAsync(_alice, "2026-10-18", 5, "API key", _now);
        await _store.AddBonusAsync(_alice, "2026-10-17", 30, "admin", _now);

        Assert.Equal(20, await _store.GetBonusMinutesAsync(_alice, "2026-10-18"));
        Assert.Equal(0, await _store.GetBonusMinutesAsync(_bob, "2026-10-18"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public async Task Bonus_TableRefusesOutOfRangeMinutes(int minutes)
    {
        await Assert.ThrowsAsync<SqliteException>(() => _store.AddBonusAsync(_alice, "2026-10-18", minutes, "admin", _now));

        Assert.Equal(0, await _store.GetBonusMinutesAsync(_alice, "2026-10-18"));
    }

    [Fact]
    public async Task PurgeUser_DeletesOnlyThatUser()
    {
        await SeedAsync(_alice, "2026-10-18");
        await SeedAsync(_bob, "2026-10-18");

        await _store.PurgeUserAsync(_alice);

        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
        Assert.Equal(0, await _store.GetBonusMinutesAsync(_alice, "2026-10-18"));
        Assert.Equal(0L, Count("reported_segment", _alice));
        Assert.Equal(1000, await _store.GetUsedMsAsync(_bob, "2026-10-18"));
        Assert.Equal(10, await _store.GetBonusMinutesAsync(_bob, "2026-10-18"));
        Assert.Equal(1L, Count("reported_segment", _bob));
    }

    [Fact]
    public async Task Prune_DeletesDaysBeforeTheCutoffAndKeepsTheCutoff()
    {
        await SeedAsync(_alice, "2026-07-19");
        await SeedAsync(_alice, "2026-07-20");
        await SeedAsync(_bob, "2026-07-18");

        var removed = await _store.PruneAsync("2026-07-20");

        Assert.Equal(6, removed);
        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, "2026-07-19"));
        Assert.Equal(1000, await _store.GetUsedMsAsync(_alice, "2026-07-20"));
        Assert.Equal(10, await _store.GetBonusMinutesAsync(_alice, "2026-07-20"));
        Assert.Equal(1L, Count("reported_segment", _alice));
        Assert.Equal(0L, Count("reported_segment", _bob));
    }

    [Fact]
    public async Task DeleteUsersNotIn_DeletesEveryoneElse()
    {
        var gone = Guid.NewGuid();
        await SeedAsync(_alice, "2026-10-18");
        await SeedAsync(gone, "2026-10-18");

        await _store.DeleteUsersNotInAsync(new HashSet<Guid> { _alice, _bob });

        Assert.Equal(1000, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
        Assert.Equal(0, await _store.GetUsedMsAsync(gone, "2026-10-18"));
        Assert.Equal(0, await _store.GetBonusMinutesAsync(gone, "2026-10-18"));
        Assert.Equal(0L, Count("reported_segment", gone));
    }

    [Fact]
    public async Task Methods_WithUnusableDatabase_Throw()
    {
        System.IO.Directory.CreateDirectory(_folder.Path);
        Execute("PRAGMA user_version = 99", initialize: false);
        using var db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        var store = new UsageStore(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetUsedMsAsync(_alice, "2026-10-18"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddUsageAsync(Amounts((_alice, "2026-10-18", 1))));
    }

    [Fact]
    public async Task AddSegments_NewSegmentsAddToTheirDayOnce()
    {
        var segments = new List<(string Id, string Day, DateTimeOffset StartedAt, int Seconds)>
        {
            ("a", "2026-10-18", _now.AddHours(-1), 120),
            ("b", "2026-10-17", _now.AddDays(-1), 60)
        };

        Assert.Equal(2, await _store.AddSegmentsAsync(_alice, segments, _now));
        Assert.Equal(0, await _store.AddSegmentsAsync(_alice, segments, _now));

        Assert.Equal(120_000, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
        Assert.Equal(60_000, await _store.GetUsedMsAsync(_alice, "2026-10-17"));
    }

    [Fact]
    public async Task AddSegments_SameIdForAnotherKid_IsTheirOwn()
    {
        var segment = new List<(string Id, string Day, DateTimeOffset StartedAt, int Seconds)> { ("a", "2026-10-18", _now, 30) };

        await _store.AddSegmentsAsync(_alice, segment, _now);
        await _store.AddSegmentsAsync(_bob, segment, _now);

        Assert.Equal(30_000, await _store.GetUsedMsAsync(_bob, "2026-10-18"));
    }

    [Fact]
    public async Task AddSegments_Concurrently_AddOnce()
    {
        var segment = new List<(string Id, string Day, DateTimeOffset StartedAt, int Seconds)> { ("same", "2026-10-18", _now, 600) };

        var added = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _store.AddSegmentsAsync(_alice, segment, _now))));

        Assert.Equal(1, added.Sum());
        Assert.Equal(600_000, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
    }

    private static Dictionary<(Guid UserId, string Day), long> Amounts(params (Guid UserId, string Day, long Ms)[] amounts)
        => amounts.ToDictionary(a => (a.UserId, a.Day), a => a.Ms);

    private async Task SeedAsync(Guid userId, string day)
    {
        await _store.AddUsageAsync(Amounts((userId, day, 1000)));
        await _store.AddBonusAsync(userId, day, 10, "admin", _now);
        Execute($"INSERT INTO reported_segment VALUES ('{userId:N}', '{Guid.NewGuid():N}', '{day}', 'x', 1, 'x')");
    }

    private long Count(string table, Guid userId)
    {
        using var connection = new SqliteConnection($"Data Source={_db.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table} WHERE user_id = $id";
        command.Parameters.AddWithValue("$id", userId.ToString("N"));
        return (long)command.ExecuteScalar()!;
    }

    private void Execute(string sql, bool initialize = true)
    {
        if (initialize)
        {
            Assert.True(_db.EnsureInitialized());
        }

        using var connection = new SqliteConnection($"Data Source={System.IO.Path.Combine(_folder.Path, KidsDatabase.FileName)};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // Reset today (feature 7)
    [Fact]
    public async Task ResetDay_ClearsTheDaysUsageAndBonus_AndSaysWhat()
    {
        await _store.AddUsageAsync(Amounts((_alice, "2026-10-18", 90_000), (_alice, "2026-10-17", 60_000), (_bob, "2026-10-18", 30_000)));
        await _store.AddBonusAsync(_alice, "2026-10-18", 15, "admin", _now);
        await _store.AddBonusAsync(_alice, "2026-10-18", 5, "admin", _now);
        await _store.AddBonusAsync(_alice, "2026-10-17", 30, "admin", _now);

        var cleared = await _store.ResetDayAsync(_alice, "2026-10-18");

        Assert.Equal((90_000L, 20), cleared);
        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
        Assert.Equal(0, await _store.GetBonusMinutesAsync(_alice, "2026-10-18"));
        Assert.Equal(60_000, await _store.GetUsedMsAsync(_alice, "2026-10-17"));
        Assert.Equal(30, await _store.GetBonusMinutesAsync(_alice, "2026-10-17"));
        Assert.Equal(30_000, await _store.GetUsedMsAsync(_bob, "2026-10-18"));
    }

    [Fact]
    public async Task ResetDay_OfAnEmptyDay_ClearsNothing()
    {
        Assert.Equal((0L, 0), await _store.ResetDayAsync(_alice, "2026-10-18"));
    }

    [Fact]
    public async Task ResetDay_KeepsReportedSegmentIds_SoARepeatAddsNothing()
    {
        var segments = new List<(string Id, string Day, DateTimeOffset StartedAt, int Seconds)> { ("a", "2026-10-18", _now.AddHours(-1), 120) };
        await _store.AddSegmentsAsync(_alice, segments, _now);

        await _store.ResetDayAsync(_alice, "2026-10-18");

        Assert.Equal(1, Count("reported_segment", _alice));
        Assert.Equal(0, await _store.AddSegmentsAsync(_alice, segments, _now));
        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, "2026-10-18"));

        // Time counted after the reset starts the day again
        await _store.AddUsageAsync(Amounts((_alice, "2026-10-18", 1000)));
        Assert.Equal(1000, await _store.GetUsedMsAsync(_alice, "2026-10-18"));
    }
}
