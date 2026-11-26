using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Hosted;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Stats;
using Jellyfin.Plugin.Kids.Storage;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

/// <summary>
/// Playback events from the session manager, through the tracker, to the daily screen time and the watch log.
/// </summary>
public sealed class PlaybackTrackerTests : IAsyncDisposable
{
    private readonly TempFolder _folder = new();
    private readonly KidsDatabase _db;
    private readonly FakeUserDirectory _users = new();
    private readonly FakeTimeProvider _time = new() { Now = new DateTimeOffset(2026, 11, 19, 17, 0, 0, TimeSpan.Zero) };
    private readonly FakeKidsSettings _settings = new() { AdvancedStats = true };
    private readonly KidRoster _roster = new();
    private readonly UsageStore _usageStore;
    private readonly StatsStore _statsStore;
    private readonly KidProfileService _profiles;
    private readonly UsageService _usage;
    private readonly WatchLog _watch;
    private readonly FakeSessionManager _sessions = FakeSessionManager.Create();
    private readonly ListLogger<PlaybackTracker> _logger = new();
    private readonly PlaybackTracker _tracker;
    private readonly Guid _kid = Guid.NewGuid();
    private readonly Episode _episode = new() { Id = Guid.NewGuid(), Name = "Taxi", SeriesId = Guid.NewGuid(), SeriesName = "Bluey", ParentIndexNumber = 2, IndexNumber = 5 };

    public PlaybackTrackerTests()
    {
        _db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        _usageStore = new UsageStore(_db);
        _statsStore = new StatsStore(_db);
        _profiles = new KidProfileService(new KidProfileStore(_db), _users, new FakePickCatalog(), new FakeLiveTvChannelCatalog(), _roster);
        _usage = new UsageService(_usageStore, _profiles, _settings, _time, NullLogger<UsageService>.Instance);
        _watch = new WatchLog(_statsStore, _roster, _settings, NullLogger<WatchLog>.Instance);
        var control = new FakePlaybackControl();
        var enforcement = new EnforcementState(_roster, _settings, control);
        var enforcer = new PlaybackEnforcer(enforcement, control, new DeliveryRegistry(), _settings, _time, NullLogger<PlaybackEnforcer>.Instance);
        _tracker = new PlaybackTracker(_sessions.Manager, _roster, _profiles, _db, _usage, _watch, enforcement, enforcer, _time, _logger);
        _users.Users.Add(_kid);
    }

    public async ValueTask DisposeAsync()
    {
        _tracker.Dispose();
        _watch.Dispose();
        _usage.Dispose();
        _db.Dispose();
        _folder.Dispose();
        await Task.CompletedTask;
    }

    private async Task StartAsync()
    {
        Assert.Equal(SaveOutcome.Saved, (await _profiles.SaveAsync(_kid, 0, new KidProfileInput())).Outcome);
        await _tracker.StartAsync(TestContext.Current.CancellationToken);
    }

    private SessionInfo Session() => new(_sessions.Manager, NullLogger.Instance)
    {
        Id = "tv-session",
        UserId = _kid,
        DeviceName = "Salón TV",
        Client = "Peke"
    };

    // Start, a minute of progress reports, stop: what a TV sends for one minute of an episode
    private void PlayOneMinute(BaseItem item)
    {
        var session = Session();
        _sessions.SendStart(new PlaybackProgressEventArgs { Session = session, Item = item, PlaySessionId = "play-1" });
        for (var i = 0; i < 6; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            _sessions.SendProgress(new PlaybackProgressEventArgs { Session = session, Item = item, PlaySessionId = "play-1" });
        }

        _sessions.SendStopped(new PlaybackStopEventArgs { Session = session, Item = item, PlaySessionId = "play-1" });
    }

    [Fact]
    public async Task PlaybackEvents_ReachTheScreenTimeAndTheWatchLog()
    {
        await StartAsync();

        PlayOneMinute(_episode);
        await _tracker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(60_000, await _usageStore.GetUsedMsAsync(_kid, "2026-11-19"));
        var segment = Assert.Single(await _statsStore.ReadSegmentsAsync(_kid, "2026-11-19", "2026-11-19", TestContext.Current.CancellationToken));
        Assert.Equal(60, segment.Seconds);
        Assert.Equal("Taxi", segment.Item.ItemName);
        Assert.Equal("Bluey", segment.Item.SeriesName);
        Assert.Equal("Salón TV", segment.Device);
        Assert.Equal("Peke", segment.Client);
        Assert.Equal(0, _watch.OpenCount);
    }

    [Fact]
    public async Task WatchLogFailure_NeverStopsTheScreenTimeCount()
    {
        await StartAsync();

        // Jellyfin has no item kind for this type, so reading it for the watch log throws
        PlayOneMinute(new UnknownKindItem { Id = Guid.NewGuid(), Name = "Odd" });
        await _tracker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(60_000, await _usageStore.GetUsedMsAsync(_kid, "2026-11-19"));
        Assert.Empty(await _statsStore.ReadSegmentsAsync(_kid, "2026-11-19", "2026-11-19", TestContext.Current.CancellationToken));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task TurnedOffDuringPlayback_TheStopStillEndsAndWritesTheSegment()
    {
        await StartAsync();
        var session = Session();
        _sessions.SendStart(new PlaybackProgressEventArgs { Session = session, Item = _episode, PlaySessionId = "play-1" });
        _time.Advance(TimeSpan.FromSeconds(30));
        _sessions.SendProgress(new PlaybackProgressEventArgs { Session = session, Item = _episode, PlaySessionId = "play-1" });

        _settings.AdvancedStats = false;
        _time.Advance(TimeSpan.FromSeconds(30));
        _sessions.SendStopped(new PlaybackStopEventArgs { Session = session, Item = _episode, PlaySessionId = "play-1" });
        await _tracker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, _watch.OpenCount);
        Assert.Equal(60_000, await _usageStore.GetUsedMsAsync(_kid, "2026-11-19"));
        Assert.Equal(30, Assert.Single(await _statsStore.ReadSegmentsAsync(_kid, "2026-11-19", "2026-11-19", TestContext.Current.CancellationToken)).Seconds);
    }

    private sealed class UnknownKindItem : Video
    {
    }
}
