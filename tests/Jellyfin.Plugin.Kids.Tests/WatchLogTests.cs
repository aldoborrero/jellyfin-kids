using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Stats;
using Jellyfin.Plugin.Kids.Storage;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class WatchLogTests : IDisposable
{
    private static readonly DateTimeOffset _t0 = new(2026, 11, 19, 17, 0, 0, TimeSpan.Zero);
    private static readonly WatchItem _taxi = new(Guid.NewGuid(), "Episode", "Taxi", Guid.NewGuid(), "Bluey", 2, 5);
    private static readonly WatchItem _hospital = _taxi with { ItemId = Guid.NewGuid(), ItemName = "Hospital", Episode = 4 };
    private static readonly WatchItem _channel = new(Guid.NewGuid(), "TvChannel", "PekeTV", ChannelId: Guid.NewGuid(), ChannelName: "PekeTV");

    private readonly TempFolder _folder = new();
    private readonly KidsDatabase _db;
    private readonly StatsStore _store;
    private readonly KidRoster _roster = new();
    private readonly FakeKidsSettings _settings = new() { AdvancedStats = true };
    private readonly WatchLog _log;
    private readonly Guid _kid = Guid.NewGuid();

    public WatchLogTests()
    {
        _db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        _store = new StatsStore(_db);
        _roster.Replace([_kid]);
        _log = new WatchLog(_store, _roster, _settings, NullLogger<WatchLog>.Instance);
    }

    public void Dispose()
    {
        _log.Dispose();
        _db.Dispose();
        _folder.Dispose();
    }

    private void Play(WatchItem item, int fromSeconds, int toSeconds, string session = "s1", string play = "p1", string device = "Salón TV")
        => _log.Played(session, play, _kid, item, device, "Peke", _t0.AddSeconds(fromSeconds), _t0.AddSeconds(toSeconds));

    private async Task<IReadOnlyList<WatchSegment>> StoredAsync()
    {
        Assert.True(await _log.FlushAsync(TestContext.Current.CancellationToken));
        return await _store.ReadSegmentsAsync(_kid, "2026-01-01", "2026-12-31", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Off_RecordsNothing()
    {
        _settings.AdvancedStats = false;
        Play(_taxi, 0, 30);
        _log.OnBreak(new BreakNotice(_kid, _t0, TimeSpan.FromMinutes(15), false));

        Assert.Equal(0, _log.OpenCount);
        Assert.Empty(await StoredAsync());
        Assert.Empty(await _store.ReadBreaksAsync(_kid, "2026-01-01", "2026-12-31", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ContinuousPlayback_IsOneSegmentWithTheCountedSeconds()
    {
        Play(_taxi, 0, 30);
        Play(_taxi, 30, 60);
        Play(_taxi, 60, 90);

        var segment = Assert.Single(await StoredAsync());
        Assert.Equal(90, segment.Seconds);
        Assert.Equal(_t0, segment.StartedAt);
        Assert.Equal(_t0.AddSeconds(90), segment.EndedAt);
        Assert.Equal("2026-11-19", segment.Day);
        Assert.Equal(_taxi, segment.Item);
        Assert.Equal("Salón TV", segment.Device);
        Assert.Equal("Peke", segment.Client);
    }

    [Fact]
    public async Task ShortPause_AndSeek_KeepTheSegment()
    {
        // A one-minute pause counts nothing, and a seek only moves the position: same playback, same segment
        Play(_taxi, 0, 30);
        Play(_taxi, 90, 120);

        var segment = Assert.Single(await StoredAsync());
        Assert.Equal(60, segment.Seconds);
        Assert.Equal(_t0.AddSeconds(120), segment.EndedAt);
    }

    [Fact]
    public async Task LongPause_SplitsTheSegment_SamePlayback()
    {
        Play(_taxi, 0, 30);
        Play(_taxi, 30 + (int)WatchLog.SegmentGap.TotalSeconds, 60 + (int)WatchLog.SegmentGap.TotalSeconds);

        var segments = await StoredAsync();
        Assert.Equal(2, segments.Count);
        Assert.NotEqual(segments[0].Id, segments[1].Id);
        Assert.Equal(segments[0].PlaybackId, segments[1].PlaybackId);
        Assert.Equal([30, 30], segments.Select(s => s.Seconds));
    }

    [Fact]
    public async Task Stop_EndsTheSegment_TheNextPlayIsNew()
    {
        Play(_taxi, 0, 30);
        _log.Stopped("s1", "p1");
        Assert.Equal(0, _log.OpenCount);
        Play(_taxi, 40, 70, play: "p2");

        var segments = await StoredAsync();
        Assert.Equal(2, segments.Count);
        Assert.NotEqual(segments[0].PlaybackId, segments[1].PlaybackId);
    }

    [Fact]
    public async Task NextEpisode_InTheSamePlaySession_IsANewPlay()
    {
        Play(_taxi, 0, 30);
        Play(_hospital, 30, 60);

        var segments = await StoredAsync();
        Assert.Equal(2, segments.Count);
        Assert.Equal(["Taxi", "Hospital"], segments.Select(s => s.Item.ItemName));
        Assert.NotEqual(segments[0].PlaybackId, segments[1].PlaybackId);
    }

    [Fact]
    public async Task TwoClients_AreTwoSegments()
    {
        Play(_taxi, 0, 30, session: "tv", device: "Salón TV");
        Play(_channel, 0, 30, session: "tablet", play: "p9", device: "Tablet");

        var segments = await StoredAsync();
        Assert.Equal(2, segments.Count);
        Assert.Equal(["Salón TV", "Tablet"], segments.Select(s => s.Device).Order());
    }

    [Fact]
    public async Task LiveTv_KeepsTheChannel()
    {
        Play(_channel, 0, 30);

        var segment = Assert.Single(await StoredAsync());
        Assert.True(segment.Item.IsLiveTv);
        Assert.Equal(_channel.ChannelId, segment.Item.ChannelId);
        Assert.Equal("PekeTV", segment.Item.ChannelName);
    }

    [Fact]
    public async Task NewDay_SplitsTheSegment()
    {
        // 23:59:40 to 00:00:20 UTC, days from midnight UTC
        var late = new DateTimeOffset(2026, 11, 19, 23, 59, 40, TimeSpan.Zero);
        _log.Played("s1", "p1", _kid, _taxi, "TV", "Peke", late, late.AddSeconds(40));

        var segments = await StoredAsync();
        Assert.Equal(["2026-11-19", "2026-11-20"], segments.Select(s => s.Day));
        Assert.Equal([20, 20], segments.Select(s => s.Seconds));
        Assert.Equal(segments[0].PlaybackId, segments[1].PlaybackId);
    }

    [Fact]
    public async Task GrowingSegment_IsUpdatedInPlace()
    {
        Play(_taxi, 0, 30);
        Assert.Single(await StoredAsync());
        Play(_taxi, 30, 60);

        var segment = Assert.Single(await StoredAsync());
        Assert.Equal(60, segment.Seconds);
        Assert.Equal(1, _log.OpenCount);
    }

    [Fact]
    public async Task Sweep_EndsIdlePlaybacks()
    {
        Play(_taxi, 0, 30);

        Assert.Equal(0, _log.Sweep(_t0.AddSeconds(30) + PlaybackClock.IdleTimeout - TimeSpan.FromSeconds(1)));
        Assert.Equal(1, _log.Sweep(_t0.AddSeconds(30) + PlaybackClock.IdleTimeout));
        Assert.Equal(0, _log.OpenCount);
        Assert.Single(await StoredAsync());
    }

    [Fact]
    public async Task FormerKid_IsNotWritten()
    {
        Play(_taxi, 0, 30);
        _roster.Remove(_kid);

        Assert.Empty(await StoredAsync());
    }

    [Fact]
    public async Task Breaks_AreKept_AndAGrownUpsEndUpdatesTheSameBreak()
    {
        _log.OnBreak(new BreakNotice(_kid, _t0, TimeSpan.FromMinutes(15), false));
        Assert.True(await _log.FlushAsync(TestContext.Current.CancellationToken));
        _log.OnBreak(new BreakNotice(_kid, _t0, TimeSpan.FromMinutes(4.6), true));
        Assert.True(await _log.FlushAsync(TestContext.Current.CancellationToken));

        var record = Assert.Single(await _store.ReadBreaksAsync(_kid, "2026-01-01", "2026-12-31", TestContext.Current.CancellationToken));
        Assert.Equal(new BreakRecord(_kid, "2026-11-19", _t0, 5, true), record);
    }

    [Fact]
    public async Task DeleteAll_EmptiesStoreAndMemory()
    {
        Play(_taxi, 0, 30);
        Assert.Single(await StoredAsync());

        Assert.Equal(1, await _log.DeleteAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, _log.OpenCount);
        Assert.Empty(await StoredAsync());
        Assert.Equal((0L, 0L, (string?)null), await _store.CountWatchLogAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedFlush_KeepsEverything_AndTheNextFlushWritesIt()
    {
        var logger = new ListLogger<WatchLog>();
        using var log = new WatchLog(_store, _roster, _settings, logger);
        log.Played("s1", "p1", _kid, _taxi, "Salón TV", "Peke", _t0, _t0.AddSeconds(30));
        log.Stopped("s1", "p1");
        log.OnBreak(new BreakNotice(_kid, _t0, TimeSpan.FromMinutes(15), false));
        Assert.True(_db.EnsureInitialized());
        Execute("ALTER TABLE watch_segment RENAME TO watch_segment_hidden");

        Assert.False(await log.FlushAsync(TestContext.Current.CancellationToken));
        Assert.False(await log.FlushAsync(TestContext.Current.CancellationToken));
        Execute("ALTER TABLE watch_segment_hidden RENAME TO watch_segment");
        Assert.True(await log.FlushAsync(TestContext.Current.CancellationToken));

        Assert.Equal(30, Assert.Single(await _store.ReadSegmentsAsync(_kid, "2026-01-01", "2026-12-31", TestContext.Current.CancellationToken)).Seconds);
        Assert.Single(await _store.ReadBreaksAsync(_kid, "2026-01-01", "2026-12-31", TestContext.Current.CancellationToken));
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task FailingFlushes_KeepAtMostMaxPending_DroppingTheOldest_AndSayItOnce()
    {
        var logger = new ListLogger<WatchLog>();
        using var log = new WatchLog(_store, _roster, _settings, logger);
        Assert.True(_db.EnsureInitialized());
        Execute("ALTER TABLE watch_segment RENAME TO watch_segment_hidden");
        for (var i = 0; i <= WatchLog.MaxPending; i++)
        {
            var session = "s" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            log.Played(session, "p", _kid, _taxi, "Salón TV", "Peke", _t0.AddSeconds(i), _t0.AddSeconds(i + 1));
            log.Stopped(session, "p");
        }

        Assert.False(await log.FlushAsync(TestContext.Current.CancellationToken));
        log.Played("late", "p", _kid, _taxi, "Salón TV", "Peke", _t0.AddDays(1), _t0.AddDays(1).AddSeconds(1));
        log.Stopped("late", "p");
        Assert.False(await log.FlushAsync(TestContext.Current.CancellationToken));
        Execute("ALTER TABLE watch_segment_hidden RENAME TO watch_segment");
        Assert.True(await log.FlushAsync(TestContext.Current.CancellationToken));

        var stored = await _store.ReadSegmentsAsync(_kid, "2026-01-01", "2026-12-31", TestContext.Current.CancellationToken);
        Assert.Equal(WatchLog.MaxPending, stored.Count);
        Assert.Equal(_t0.AddSeconds(2), stored.Min(s => s.StartedAt));
        Assert.Contains(stored, s => s.StartedAt == _t0.AddDays(1));
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("dropp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnusableDatabase_RecordsNothing()
    {
        using var folder = new TempFolder();
        string path;
        using (var db = new KidsDatabase(folder.Path, NullLogger<KidsDatabase>.Instance))
        {
            Assert.True(db.EnsureInitialized());
            path = db.DatabasePath;
        }

        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99";
            command.ExecuteNonQuery();
        }

        using var unusable = new KidsDatabase(folder.Path, NullLogger<KidsDatabase>.Instance);
        Assert.False(unusable.EnsureInitialized());
        using var log = new WatchLog(new StatsStore(unusable), _roster, _settings, NullLogger<WatchLog>.Instance);

        log.Played("s1", "p1", _kid, _taxi, "Salón TV", "Peke", _t0, _t0.AddSeconds(30));
        log.OnBreak(new BreakNotice(_kid, _t0, TimeSpan.FromMinutes(15), false));

        Assert.Equal(0, log.OpenCount);
        Assert.Equal(0, log.PendingCount);
    }

    [Fact]
    public async Task TurnedOffDuringPlayback_TheOpenSegmentIsStillEndedAndWritten()
    {
        Play(_taxi, 0, 30);
        _settings.AdvancedStats = false;
        Play(_taxi, 30, 60);

        Assert.Equal(1, _log.Sweep(_t0.AddSeconds(30) + PlaybackClock.IdleTimeout));
        var segment = Assert.Single(await StoredAsync());
        Assert.Equal(30, segment.Seconds);
        Assert.Equal(0, _log.OpenCount);
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_db.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
