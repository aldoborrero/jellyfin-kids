using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Kids.Allowance;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class PlaybackClockTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly PlaybackClock _clock = new();
    private readonly Guid _alice = Guid.NewGuid();
    private readonly List<(Guid UserId, DateTimeOffset From, DateTimeOffset To)> _counted = [];

    private TimeSpan Total(Guid? userId = null)
        => TimeSpan.FromTicks(_counted.Where(c => userId is null || c.UserId == userId).Sum(c => (c.To - c.From).Ticks));

    private void Send(PlaybackEventKind kind, double atSeconds, bool paused = false, bool automated = false, string session = "s1", string? play = "p1", Guid? owner = null)
        => _counted.AddRange(_clock.Apply(new PlaybackEvent(kind, session, play, owner ?? _alice, paused, automated, _t0.AddSeconds(atSeconds))));

    [Fact]
    public void Playing_CountsWallClock()
    {
        Send(PlaybackEventKind.Start, 0);
        for (var t = 10; t <= 60; t += 10)
        {
            Send(PlaybackEventKind.Progress, t);
        }

        Send(PlaybackEventKind.Stop, 60);

        Assert.Equal(TimeSpan.FromSeconds(60), Total());
        Assert.All(_counted, c => Assert.Equal(_alice, c.UserId));
    }

    [Fact]
    public void SyntheticTicks_Count()
    {
        Send(PlaybackEventKind.Start, 0);
        Send(PlaybackEventKind.Progress, 0);
        for (var t = 1; t <= 30; t++)
        {
            Send(PlaybackEventKind.Progress, t, automated: true);
        }

        Assert.Equal(TimeSpan.FromSeconds(30), Total());
    }

    [Fact]
    public void OneGap_IsCappedAtThirtySeconds()
    {
        Send(PlaybackEventKind.Start, 0);
        Send(PlaybackEventKind.Progress, 300);

        Assert.Equal(TimeSpan.FromSeconds(30), Total());
    }

    [Fact]
    public void PausedTime_DoesNotCount()
    {
        Send(PlaybackEventKind.Start, 0);
        Send(PlaybackEventKind.Progress, 10, paused: true);
        Send(PlaybackEventKind.Progress, 20, paused: true);
        Send(PlaybackEventKind.Progress, 30, paused: true);
        Send(PlaybackEventKind.Progress, 40);
        Send(PlaybackEventKind.Progress, 50);

        Assert.Equal(TimeSpan.FromSeconds(20), Total());
    }

    [Fact]
    public void DeadClient_StopsCountingSixtySecondsAfterTheLastRealReport()
    {
        Send(PlaybackEventKind.Start, 0);
        for (var t = 1; t <= 120; t++)
        {
            Send(PlaybackEventKind.Progress, t, automated: true);
        }

        Assert.Equal(TimeSpan.FromSeconds(60), Total());

        // A real report brings it back: counting resumes from that report
        Send(PlaybackEventKind.Progress, 130);
        Send(PlaybackEventKind.Progress, 131, automated: true);
        Send(PlaybackEventKind.Progress, 132, automated: true);

        Assert.Equal(TimeSpan.FromSeconds(62), Total());
    }

    [Fact]
    public void ConcurrentPlaybacks_EachCount()
    {
        Send(PlaybackEventKind.Start, 0, session: "tv");
        Send(PlaybackEventKind.Start, 0, session: "tablet");
        for (var t = 10; t <= 30; t += 10)
        {
            Send(PlaybackEventKind.Progress, t, session: "tv");
            Send(PlaybackEventKind.Progress, t, session: "tablet");
        }

        Assert.Equal(TimeSpan.FromSeconds(60), Total(_alice));
        Assert.Equal(6, _counted.Count);
        Assert.All(_counted, c => Assert.Equal(TimeSpan.FromSeconds(10), c.To - c.From));
    }

    [Fact]
    public void SameSessionDifferentPlaySessions_AreSeparate()
    {
        Send(PlaybackEventKind.Start, 0, play: "a");
        Send(PlaybackEventKind.Start, 0, play: null);
        Send(PlaybackEventKind.Progress, 10, play: "a");
        Send(PlaybackEventKind.Progress, 10, play: null);

        Assert.Equal(TimeSpan.FromSeconds(20), Total());
    }

    [Fact]
    public void OutOfOrderEvent_AddsNothingAndDoesNotMoveBack()
    {
        Send(PlaybackEventKind.Start, 0);
        Send(PlaybackEventKind.Progress, 20);
        Send(PlaybackEventKind.Progress, 10);
        Send(PlaybackEventKind.Progress, 25);

        Assert.Equal(TimeSpan.FromSeconds(25), Total());
    }

    [Fact]
    public void ProgressForAnUnknownKey_StartsARecordAndAddsNothing()
    {
        Send(PlaybackEventKind.Progress, 0);
        Assert.Empty(_counted);

        Send(PlaybackEventKind.Progress, 10);
        Assert.Equal(TimeSpan.FromSeconds(10), Total());
    }

    [Fact]
    public void Stop_CountsTheLastGapThenRemoves()
    {
        Send(PlaybackEventKind.Start, 0);
        Send(PlaybackEventKind.Stop, 8);
        Send(PlaybackEventKind.Stop, 12);

        Assert.Equal(TimeSpan.FromSeconds(8), Total());
        Assert.Equal(0, _clock.Count);
    }

    [Fact]
    public void Sweep_RemovesIdleRecords()
    {
        Send(PlaybackEventKind.Start, 0, session: "old");
        Send(PlaybackEventKind.Start, 300, session: "recent");

        var removed = _clock.Sweep(_t0.AddMinutes(6));

        Assert.Equal(1, removed);
        Assert.Equal(1, _clock.Count);
        Send(PlaybackEventKind.Progress, 370, session: "old");
        Assert.Empty(_counted);
    }

    [Fact]
    public void Intervals_AreTheCountedWallClockRanges()
    {
        Send(PlaybackEventKind.Start, 0);
        Send(PlaybackEventKind.Progress, 10);

        Assert.Equal([(_alice, _t0, _t0.AddSeconds(10))], _counted);
    }
}
