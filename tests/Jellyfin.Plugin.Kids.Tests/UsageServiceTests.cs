using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class UsageServiceTests : IDisposable
{
    private static readonly CallerContext _admin = new(Guid.NewGuid(), IsAdmin: true, IsApiKey: false, UserName: "admin");
    private static readonly CallerContext _apiKey = new(Guid.Empty, IsAdmin: true, IsApiKey: true);

    private readonly TempFolder _folder = new();
    private readonly KidsDatabase _db;
    private readonly UsageStore _store;
    private readonly FakeUserDirectory _users = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeKidsSettings _settings = new();
    private readonly KidProfileService _profiles;
    private readonly UsageService _service;
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    public UsageServiceTests()
    {
        _db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        _store = new UsageStore(_db);
        _profiles = new KidProfileService(new KidProfileStore(_db), _users, new FakePickCatalog(), new FakeLiveTvChannelCatalog(), new KidRoster());
        _service = new UsageService(_store, _profiles, _settings, _time, NullLogger<UsageService>.Instance);
        _users.Users.Add(_alice);
        _users.Users.Add(_bob);

        // 2026-10-18 09:00 UTC, days from midnight UTC
        _time.Now = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    }

    private static string Today => "2026-10-18";

    public void Dispose()
    {
        _service.Dispose();
        _db.Dispose();
        _folder.Dispose();
    }

    private async Task MarkAsync(Guid userId, int? allowance = null)
    {
        var result = await _profiles.SaveAsync(userId, 0, new KidProfileInput { DailyAllowanceMinutes = allowance });
        Assert.Equal(SaveOutcome.Saved, result.Outcome);
    }

    private async Task<KidStatus> StatusAsync(Guid userId) => (await _service.GetStatusAsync(userId, TestContext.Current.CancellationToken))!;

    // Status (US2)
    [Fact]
    public async Task Status_ReadsTodaysStoredUsage()
    {
        await MarkAsync(_alice, 30);
        await _store.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long> { [(_alice, Today)] = 1_500_500 });

        var status = await StatusAsync(_alice);

        Assert.Equal(new DateOnly(2026, 10, 18), status.Date);
        Assert.Equal(1500, status.UsedSeconds);
        Assert.Equal(1800, status.AllowanceSeconds);
        Assert.Equal(300, status.RemainingSeconds);
        Assert.Equal(_time.Now, status.ServerTime);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), status.NextDayStartsAt);
        Assert.Equal([300, 60], status.WarnAtSeconds);
    }

    [Fact]
    public async Task Status_LeavesOutYesterday()
    {
        await MarkAsync(_alice);
        await _store.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long> { [(_alice, "2026-10-17")] = 600_000 });
        await _store.AddBonusAsync(_alice, "2026-10-17", 10, "admin", _time.Now);

        var status = await StatusAsync(_alice);

        Assert.Equal(0, status.UsedSeconds);
        Assert.Equal(0, status.BonusSeconds);
    }

    [Fact]
    public async Task Status_NotAKid_IsNull()
    {
        Assert.Null(await _service.GetStatusAsync(_alice, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Status_UnusableStore_Throws()
    {
        Directory.CreateDirectory(_folder.Path);
        using var broken = new TempFolder();
        Directory.CreateDirectory(broken.Path);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(broken.Path, KidsDatabase.FileName)};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99";
            command.ExecuteNonQuery();
        }

        using var db = new KidsDatabase(broken.Path, NullLogger<KidsDatabase>.Instance);
        var profiles = new KidProfileService(new KidProfileStore(db), _users, new FakePickCatalog(), new FakeLiveTvChannelCatalog(), new KidRoster());
        using var service = new UsageService(new UsageStore(db), profiles, _settings, _time, NullLogger<UsageService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStatusAsync(_alice, TestContext.Current.CancellationToken));
    }

    // Counting (US1)
    [Fact]
    public async Task AddPlayed_IsInTheStatusBeforeAnyFlush()
    {
        await MarkAsync(_alice);

        _service.AddPlayed(_alice, _time.Now.AddSeconds(-30), _time.Now);

        Assert.Equal(30, (await StatusAsync(_alice)).UsedSeconds);
        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, Today));
    }

    [Fact]
    public async Task AddPlayed_AcrossTheDayStart_CountsOnBothDays()
    {
        await MarkAsync(_alice);
        var midnight = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);

        _service.AddPlayed(_alice, midnight.AddSeconds(-20), midnight.AddSeconds(10));
        await _service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(20_000, await _store.GetUsedMsAsync(_alice, "2026-10-17"));
        Assert.Equal(10_000, await _store.GetUsedMsAsync(_alice, Today));
    }

    [Fact]
    public async Task Flush_WritesPendingOnceAndClearsIt()
    {
        await MarkAsync(_alice);
        _service.AddPlayed(_alice, _time.Now.AddSeconds(-30), _time.Now);
        _service.AddPlayed(_bob, _time.Now.AddSeconds(-10), _time.Now);

        Assert.True(await _service.FlushAsync(TestContext.Current.CancellationToken));
        Assert.True(await _service.FlushAsync(TestContext.Current.CancellationToken));

        Assert.Equal(30_000, await _store.GetUsedMsAsync(_alice, Today));
        Assert.Equal(10_000, await _store.GetUsedMsAsync(_bob, Today));
        Assert.Equal(30, (await StatusAsync(_alice)).UsedSeconds);
        Assert.Equal(0, _service.PendingCount);
    }

    [Fact]
    public async Task Flush_Failing_KeepsPendingAndTheNextFlushWritesItOnce()
    {
        await MarkAsync(_alice);
        Execute("CREATE TRIGGER fail_usage BEFORE INSERT ON usage_day BEGIN SELECT RAISE(ABORT, 'test'); END");
        _service.AddPlayed(_alice, _time.Now.AddSeconds(-30), _time.Now);

        Assert.False(await _service.FlushAsync(TestContext.Current.CancellationToken));
        Assert.Equal(30, (await StatusAsync(_alice)).UsedSeconds);

        Execute("DROP TRIGGER fail_usage");
        _service.AddPlayed(_alice, _time.Now, _time.Now.AddSeconds(5));
        Assert.True(await _service.FlushAsync(TestContext.Current.CancellationToken));

        Assert.Equal(35_000, await _store.GetUsedMsAsync(_alice, Today));
        Assert.Equal(35, (await StatusAsync(_alice)).UsedSeconds);
    }

    [Fact]
    public async Task DropPending_RemovesOnlyThatUser()
    {
        _service.AddPlayed(_alice, _time.Now.AddSeconds(-30), _time.Now);
        _service.AddPlayed(_bob, _time.Now.AddSeconds(-10), _time.Now);

        _service.DropPending(_alice);
        await _service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, Today));
        Assert.Equal(10_000, await _store.GetUsedMsAsync(_bob, Today));
    }

    // Bonus (US4)
    [Fact]
    public async Task Bonus_StoresOnTodayAndReturnsTheStatus()
    {
        await MarkAsync(_alice, 30);

        var result = await _service.AddBonusAsync(_admin, _alice, 15, TestContext.Current.CancellationToken);

        Assert.Equal(UsageOutcome.Saved, result.Outcome);
        Assert.Equal(900, result.Status!.BonusSeconds);
        Assert.Equal(2700, result.Status.RemainingSeconds);
        Assert.Equal(15, await _store.GetBonusMinutesAsync(_alice, Today));
        Assert.Equal("admin", ReadScalar<string>("SELECT granted_by FROM bonus"));
    }

    [Fact]
    public async Task Bonus_ByApiKey_IsRecordedAsApiKeyAndAddsUp()
    {
        await MarkAsync(_alice, 30);

        await _service.AddBonusAsync(_admin, _alice, 10, TestContext.Current.CancellationToken);
        var result = await _service.AddBonusAsync(_apiKey, _alice, 5, TestContext.Current.CancellationToken);

        Assert.Equal(900, result.Status!.BonusSeconds);
        Assert.Equal(1L, ReadScalar<long>("SELECT count(*) FROM bonus WHERE granted_by = 'API key'"));
    }

    [Fact]
    public async Task Bonus_ExpiresAtTheNextDayStart()
    {
        await MarkAsync(_alice, 30);
        await _service.AddBonusAsync(_admin, _alice, 15, TestContext.Current.CancellationToken);

        _time.Now = (await StatusAsync(_alice)).NextDayStartsAt;

        Assert.Equal(0, (await StatusAsync(_alice)).BonusSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    [InlineData(null)]
    public async Task Bonus_OutOfRange_IsInvalidAndStoresNothing(int? minutes)
    {
        await MarkAsync(_alice, 30);

        var result = await _service.AddBonusAsync(_admin, _alice, minutes, TestContext.Current.CancellationToken);

        Assert.Equal(UsageOutcome.Invalid, result.Outcome);
        Assert.Contains("Minutes", result.Errors!.Keys);
        Assert.Equal(0, await _store.GetBonusMinutesAsync(_alice, Today));
    }

    [Fact]
    public async Task Bonus_NotAKid_IsNotAKid()
    {
        var result = await _service.AddBonusAsync(_admin, _alice, 15, TestContext.Current.CancellationToken);

        Assert.Equal(UsageOutcome.NotAKid, result.Outcome);
    }

    // Reports (US5)
    [Fact]
    public async Task Report_AddsToTheDayTheSegmentStartedIn()
    {
        await MarkAsync(_alice);
        var segments = new[]
        {
            new SegmentInput("today", _time.Now.AddHours(-1), 600),
            new SegmentInput("yesterday", new DateTimeOffset(2026, 10, 17, 23, 50, 0, TimeSpan.Zero), 1200)
        };

        var result = await _service.ReportAsync(_alice, segments, TestContext.Current.CancellationToken);

        Assert.Equal(UsageOutcome.Saved, result.Outcome);
        Assert.Equal(600, result.Status!.UsedSeconds);
        Assert.Equal(1_200_000, await _store.GetUsedMsAsync(_alice, "2026-10-17"));
    }

    [Fact]
    public async Task Report_Repeated_AddsOnce()
    {
        await MarkAsync(_alice);
        var segments = new[] { new SegmentInput("a", _time.Now.AddHours(-1), 120) };

        await _service.ReportAsync(_alice, segments, TestContext.Current.CancellationToken);
        var again = await _service.ReportAsync(_alice, segments, TestContext.Current.CancellationToken);

        Assert.Equal(120, again.Status!.UsedSeconds);
    }

    [Fact]
    public async Task Report_Invalid_AddsNothingToAnyDay()
    {
        await MarkAsync(_alice);
        var segments = new[] { new SegmentInput("good", _time.Now.AddHours(-1), 120), new SegmentInput("bad", _time.Now.AddHours(-1), 0) };

        var result = await _service.ReportAsync(_alice, segments, TestContext.Current.CancellationToken);

        Assert.Equal(UsageOutcome.Invalid, result.Outcome);
        Assert.Contains("Segments[1].Seconds", result.Errors!.Keys);
        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, Today));
        Assert.Equal(0L, ReadScalar<long>("SELECT count(*) FROM reported_segment"));
    }

    [Fact]
    public async Task Report_NeverLowersUsage()
    {
        await MarkAsync(_alice);
        await _store.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long> { [(_alice, Today)] = 300_000 });

        await _service.ReportAsync(_alice, [new SegmentInput("x", _time.Now, 1)], TestContext.Current.CancellationToken);
        await _service.ReportAsync(_alice, [new SegmentInput("y", _time.Now, -100)], TestContext.Current.CancellationToken);

        Assert.Equal(301_000, await _store.GetUsedMsAsync(_alice, Today));
    }

    [Fact]
    public async Task Report_NotAKid_IsNotAKid()
    {
        var result = await _service.ReportAsync(_alice, [new SegmentInput("a", _time.Now, 60)], TestContext.Current.CancellationToken);

        Assert.Equal(UsageOutcome.NotAKid, result.Outcome);
        Assert.Equal(0L, ReadScalar<long>("SELECT count(*) FROM reported_segment"));
    }

    // Day start setting (US6)
    [Fact]
    public async Task ChangingTheDayStart_AppliesToTheNextReadWithoutRecalculating()
    {
        await MarkAsync(_alice);
        await _store.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long> { [(_alice, Today)] = 60_000 });

        _settings.DayStartsAtHour = 10;
        var status = await StatusAsync(_alice);

        Assert.Equal(new DateOnly(2026, 10, 17), status.Date);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero), status.NextDayStartsAt);
        Assert.Equal(0, status.UsedSeconds);
        Assert.Equal(60_000, await _store.GetUsedMsAsync(_alice, Today));
    }

    // Purge (FR-022)
    [Fact]
    public async Task Unmark_WithoutPurge_KeepsTodaysUsage()
    {
        await MarkAsync(_alice);
        _service.AddPlayed(_alice, _time.Now.AddSeconds(-30), _time.Now);
        await _service.FlushAsync(TestContext.Current.CancellationToken);

        await _profiles.DeleteAsync(_alice);
        await MarkAsync(_alice);

        Assert.Equal(30, (await StatusAsync(_alice)).UsedSeconds);
    }

    [Fact]
    public async Task Purge_RemovesStoredAndPending()
    {
        await MarkAsync(_alice);
        _service.AddPlayed(_alice, _time.Now.AddSeconds(-30), _time.Now);
        await _service.FlushAsync(TestContext.Current.CancellationToken);
        await _service.AddBonusAsync(_admin, _alice, 10, TestContext.Current.CancellationToken);
        _service.AddPlayed(_alice, _time.Now, _time.Now.AddSeconds(5));

        await _profiles.DeleteAsync(_alice);
        await _service.PurgeAsync(_alice, TestContext.Current.CancellationToken);
        await _service.FlushAsync(TestContext.Current.CancellationToken);
        await MarkAsync(_alice);

        var status = await StatusAsync(_alice);
        Assert.Equal(0, status.UsedSeconds);
        Assert.Equal(0, status.BonusSeconds);
    }

    // Retention (FR-021)
    [Fact]
    public async Task Prune_RemovesDaysOlderThan90AndKeeps90()
    {
        var old = DayClock.Format(new DateOnly(2026, 10, 18).AddDays(-91));
        var kept = DayClock.Format(new DateOnly(2026, 10, 18).AddDays(-90));
        await _store.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long> { [(_alice, old)] = 1000, [(_alice, kept)] = 2000 });

        Assert.Equal(1, await _service.PruneAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, await _store.GetUsedMsAsync(_alice, old));
        Assert.Equal(2000, await _store.GetUsedMsAsync(_alice, kept));
    }

    private T ReadScalar<T>(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_db.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)command.ExecuteScalar()!;
    }

    private void Execute(string sql)
    {
        Assert.True(_db.EnsureInitialized());
        using var connection = new SqliteConnection($"Data Source={_db.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // Reset today (feature 7)
    [Fact]
    public async Task ResetDay_ClearsStoredAndPendingTimeAndTheBonus()
    {
        await MarkAsync(_alice, 30);
        await _store.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long> { [(_alice, Today)] = 1_800_000 });
        await _service.AddBonusAsync(_admin, _alice, 10, TestContext.Current.CancellationToken);
        _service.AddPlayed(_alice, _time.Now.AddSeconds(-30), _time.Now);
        var changed = new List<Guid>();
        _service.UsageChanged += (_, e) => changed.Add(e.Argument);

        var reset = await _service.ResetDayAsync(_alice, TestContext.Current.CancellationToken);

        Assert.NotNull(reset);
        Assert.Equal(Today, reset.Day);
        Assert.Equal(1_830_000, reset.UsedMs);
        Assert.Equal(10, reset.BonusMinutes);
        Assert.Equal(0, reset.Status.UsedSeconds);
        Assert.Equal(0, reset.Status.BonusSeconds);
        Assert.False(reset.Status.TimeUp);
        Assert.Equal(1800, reset.Status.RemainingSeconds);
        Assert.Equal([_alice], changed);

        // Nothing pending is left for a flush to write back
        Assert.True(await _service.FlushAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, (await StatusAsync(_alice)).UsedSeconds);
    }

    [Fact]
    public async Task ResetDay_KeepsOtherDaysAndOtherKids()
    {
        await MarkAsync(_alice);
        await MarkAsync(_bob);
        await _store.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long>
        {
            [(_alice, "2026-10-17")] = 600_000,
            [(_bob, Today)] = 120_000,
        });
        _service.AddPlayed(_bob, _time.Now.AddSeconds(-30), _time.Now);

        await _service.ResetDayAsync(_alice, TestContext.Current.CancellationToken);

        Assert.Equal(600_000, await _store.GetUsedMsAsync(_alice, "2026-10-17"));
        Assert.Equal(150, (await StatusAsync(_bob)).UsedSeconds);
    }

    [Fact]
    public async Task ResetDay_NotAKid_IsNull()
    {
        Assert.Null(await _service.ResetDayAsync(_alice, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetDay_AllowanceOfZero_StaysTimeUp()
    {
        await MarkAsync(_alice, 0);

        var reset = await _service.ResetDayAsync(_alice, TestContext.Current.CancellationToken);

        Assert.True(reset!.Status.TimeUp);
    }

    [Fact]
    public async Task ResetDay_StoreFailure_KeepsEverything()
    {
        await MarkAsync(_alice, 30);
        await _store.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long> { [(_alice, Today)] = 600_000 });
        _service.AddPlayed(_alice, _time.Now.AddSeconds(-30), _time.Now);
        Execute("CREATE TRIGGER fail_reset BEFORE DELETE ON usage_day BEGIN SELECT RAISE(ABORT, 'test'); END");

        await Assert.ThrowsAnyAsync<Exception>(() => _service.ResetDayAsync(_alice, TestContext.Current.CancellationToken));

        Assert.Equal(630, (await StatusAsync(_alice)).UsedSeconds);
        Assert.Equal(1, _service.PendingCount);
    }
}
