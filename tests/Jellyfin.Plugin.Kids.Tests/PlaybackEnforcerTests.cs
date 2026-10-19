using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class PlaybackEnforcerTests
{
    private static readonly Guid Kid = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 19);

    private readonly KidRoster _roster = new();
    private readonly FakeKidsSettings _settings = new() { EnforcementEnabled = true };
    private readonly FakePlaybackControl _control = new();
    private readonly FakeTimeProvider _time = new() { Now = new DateTimeOffset(2026, 10, 19, 16, 0, 0, TimeSpan.Zero) };
    private readonly DeliveryRegistry _deliveries = new();
    private readonly ListLogger<PlaybackEnforcer> _logger = new();
    private readonly EnforcementState _state;
    private readonly PlaybackEnforcer _enforcer;
    private readonly KidSession _tv = new("tv", Kid, "alice", "Wholphin", "dev-tv", true, false, "live-1");
    private readonly KidSession _web = new("web", Kid, "alice", "Jellyfin Web", "dev-web", true, false, null);

    public PlaybackEnforcerTests()
    {
        _roster.Replace([Kid]);
        _state = new EnforcementState(_roster, _settings, _control);
        _enforcer = new PlaybackEnforcer(_state, _control, _deliveries, _settings, _time, _logger) { MessageGrace = TimeSpan.Zero };
    }

    [Fact]
    public async Task Denied_RunsTheWholeChainOnce_InOrder()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);

        await _enforcer.OnPlayback(_tv, automated: true);
        await _enforcer.OnPlayback(_tv, automated: true);

        Assert.Equal(
            ["dialog tv Time's up: Screen time is over for today.", "stop tv", "kill dev-tv", "close live-1", "end tv"],
            _control.Snapshot());
    }

    [Fact]
    public async Task Denied_CutsTheKidsDeliveries()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var lifetime = new DeliveryRegistryTests.FakeLifetime();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature>(lifetime);
        using var delivery = _deliveries.Track(Kid, context);

        await _enforcer.OnPlayback(_web, automated: true);

        Assert.True(lifetime.Aborted);
    }

    [Fact]
    public async Task RealReportsWhileDenied_RepeatTheStopAtMostEvery10Seconds()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);
        await _enforcer.OnPlayback(_web, automated: false);
        _control.Calls.Clear();

        _time.Advance(TimeSpan.FromSeconds(5));
        await _enforcer.OnPlayback(_web, automated: false);
        Assert.Empty(_control.Snapshot());

        _time.Advance(TimeSpan.FromSeconds(5));
        await _enforcer.OnPlayback(_web, automated: false);
        Assert.Equal(["stop web", "kill dev-web", "end web"], _control.Snapshot());
    }

    [Fact]
    public async Task AutomatedTicksWhileDenied_NeverRepeat()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);
        await _enforcer.OnPlayback(_web, automated: true);
        _control.Calls.Clear();

        for (var i = 0; i < 60; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await _enforcer.OnPlayback(_web, automated: true);
        }

        Assert.Empty(_control.Snapshot());
    }

    [Fact]
    public async Task EachSessionIsStopped_AndTheDenialIsLoggedOnce()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);

        await _enforcer.OnPlayback(_tv, automated: true);
        await _enforcer.OnPlayback(_web, automated: true);
        for (var i = 0; i < 30; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await _enforcer.OnPlayback(_web, automated: i % 2 == 0);
        }

        Assert.Contains("stop tv", _control.Snapshot());
        Assert.Contains("stop web", _control.Snapshot());
        var info = _logger.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Message).ToList();
        Assert.Single(info, m => m.Contains("may not play now", StringComparison.Ordinal));
        Assert.Equal(2, info.Count(m => m.Contains("stopped alice's playback on", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EnforcementOff_DoesNothing()
    {
        _settings.EnforcementEnabled = false;
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);
        _control.Current.Add(_web);

        await _enforcer.OnPlayback(_web, automated: false);
        await _enforcer.Sweep();

        Assert.Empty(_control.Snapshot());
    }

    [Fact]
    public async Task Warnings_AreSentOncePerThreshold()
    {
        // 200 s left: the 5-minute notice (it says 4 minutes), then the 1-minute one
        _state.SetKid(Kid, new KidBudget(Today, (30 * 60_000) - 200_000, 30, 0), null, null);

        await _enforcer.OnPlayback(_web, automated: true);
        await _enforcer.OnPlayback(_web, automated: true);
        Assert.Equal(["toast web Screen time: 4 minutes left"], _control.Snapshot());

        _state.OnPlayed(Kid, _time.Now, _time.Now.AddSeconds(150));
        _time.Advance(TimeSpan.FromSeconds(150));
        await _enforcer.OnPlayback(_web, automated: true);
        await _enforcer.OnPlayback(_web, automated: true);

        Assert.Equal(["toast web Screen time: 4 minutes left", "toast web Screen time: 1 minute left"], _control.Snapshot());
    }

    [Fact]
    public async Task StartingWithLessThanAMinute_SendsOnlyTheLastWarning()
    {
        _state.SetKid(Kid, new KidBudget(Today, (30 * 60_000) - 30_000, 30, 0), null, null);

        await _enforcer.OnPlayback(_web, automated: true);
        _time.Advance(TimeSpan.FromSeconds(1));
        await _enforcer.OnPlayback(_web, automated: true);

        Assert.Equal(["toast web Screen time: 1 minute left"], _control.Snapshot());
    }

    [Fact]
    public async Task SittingLimit_StopsWithTheBreakMessage()
    {
        _settings.SittingBreak = TimeSpan.FromMinutes(15);
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), 30, null);
        _state.OnPlayed(Kid, _time.Now.AddMinutes(-30), _time.Now);

        await _enforcer.OnPlayback(_web, automated: true);

        Assert.Equal(
            ["dialog web Time for a break: You can watch again in 15 minutes.", "stop web", "kill dev-web", "end web"],
            _control.Snapshot());
    }

    [Fact]
    public async Task Sweep_StopsDeniedSessionsStillShownPlaying_AndSkipsPausedOnes()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);
        _control.Current.Add(_web);
        _control.Current.Add(_tv with { IsPaused = true });
        _control.Current.Add(new KidSession("adult", Guid.NewGuid(), "admin", "web", "dev-admin", true, false, null));

        await _enforcer.Sweep();

        var calls = _control.Snapshot();
        Assert.Contains("stop web", calls);
        Assert.DoesNotContain("stop tv", calls);
        Assert.DoesNotContain(calls, c => c.Contains("adult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnforcementSwitchedOffDuringTheMessage_StopsNothing()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);
        var grace = new TaskCompletionSource();
        _enforcer.MessageGrace = TimeSpan.FromSeconds(5);
        _enforcer.Wait = _ => grace.Task;

        var chain = _enforcer.OnPlayback(_web, automated: true);
        _settings.EnforcementEnabled = false;
        grace.SetResult();
        await chain;

        Assert.Equal(["dialog web Time's up: Screen time is over for today."], _control.Snapshot());
    }

    [Fact]
    public async Task DenialLiftedDuringTheMessage_StopsNothing()
    {
        _state.SetKid(Kid, new KidBudget(Today, 30 * 60_000, 30, 0), null, null);
        var grace = new TaskCompletionSource();
        _enforcer.MessageGrace = TimeSpan.FromSeconds(5);
        _enforcer.Wait = _ => grace.Task;
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var lifetime = new DeliveryRegistryTests.FakeLifetime();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature>(lifetime);

        // The message is shown and the chain waits; meanwhile a bonus arrives and the kid starts a new stream
        var chain = _enforcer.OnPlayback(_web, automated: true);
        _state.SetKid(Kid, new KidBudget(Today, 30 * 60_000, 30, 5), null, null);
        using var delivery = _deliveries.Track(Kid, context);
        grace.SetResult();
        await chain;

        Assert.Equal(["dialog web Time's up: Screen time is over for today."], _control.Snapshot());
        Assert.False(lifetime.Aborted);

        // A later denial runs the whole chain again
        _enforcer.Wait = _ => Task.CompletedTask;
        _state.OnPlayed(Kid, _time.Now, _time.Now.AddMinutes(5));
        await _enforcer.OnPlayback(_web, automated: true);
        Assert.Equal(
            ["dialog web Time's up: Screen time is over for today.", "stop web", "kill dev-web", "end web"],
            _control.Snapshot().Skip(1).ToList());
        Assert.True(lifetime.Aborted);
    }

    [Fact]
    public async Task BonusLiftsTheDenial_AndANewDenialStopsAgain()
    {
        _state.SetKid(Kid, new KidBudget(Today, 30 * 60_000, 30, 0), null, null);
        await _enforcer.OnPlayback(_web, automated: true);
        _state.SetKid(Kid, new KidBudget(Today, 30 * 60_000, 30, 5), null, null);
        await _enforcer.OnPlayback(_web, automated: true);

        // The bonus left exactly 5 minutes: no further stop, only the 5-minute notice
        Assert.Equal(
            ["dialog web Time's up: Screen time is over for today.", "stop web", "kill dev-web", "end web", "toast web Screen time: 5 minutes left"],
            _control.Snapshot());
        _control.Calls.Clear();

        _state.OnPlayed(Kid, _time.Now, _time.Now.AddMinutes(5));
        await _enforcer.OnPlayback(_web, automated: true);

        Assert.Equal(["dialog web Time's up: Screen time is over for today.", "stop web", "kill dev-web", "end web"], _control.Snapshot());
    }
}
