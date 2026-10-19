using System;
using Jellyfin.Plugin.Kids.Enforcement;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class SittingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 16, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Break = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(30);

    [Fact]
    public void Empty_HasNothingUsed()
    {
        var state = new Sitting().Read(T0, Limit, Break);

        Assert.Equal(0, state.UsedMs);
        Assert.Equal((long)Limit.TotalMilliseconds, state.RemainingMs);
        Assert.Null(state.BreakEndsAt);
        Assert.Null(state.StartedAt);
    }

    [Fact]
    public void ShortGaps_KeepTheSitting()
    {
        var sitting = new Sitting();
        sitting.Add(T0, T0.AddMinutes(10), Break);
        sitting.Add(T0.AddMinutes(20), T0.AddMinutes(30), Break);

        var state = sitting.Read(T0.AddMinutes(30), Limit, Break);
        Assert.Equal(20 * 60_000, state.UsedMs);
        Assert.Equal(T0, state.StartedAt);
    }

    [Fact]
    public void BreakOfTheFullLength_StartsANewSitting()
    {
        var sitting = new Sitting();
        sitting.Add(T0, T0.AddMinutes(10), Break);
        sitting.Add(T0.AddMinutes(25), T0.AddMinutes(26), Break);

        var state = sitting.Read(T0.AddMinutes(26), Limit, Break);
        Assert.Equal(60_000, state.UsedMs);
        Assert.Equal(T0.AddMinutes(25), state.StartedAt);
    }

    [Fact]
    public void TwoDevicesAtOnce_BothCount()
    {
        var sitting = new Sitting();
        sitting.Add(T0, T0.AddMinutes(10), Break);
        sitting.Add(T0.AddMinutes(5), T0.AddMinutes(10), Break);

        Assert.Equal(15 * 60_000, sitting.Read(T0.AddMinutes(10), Limit, Break).UsedMs);
        Assert.Equal(T0.AddMinutes(10), sitting.LastPlayedAt);
    }

    [Fact]
    public void LimitReached_BreakEndsAfterTheLastPlay()
    {
        var sitting = new Sitting();
        sitting.Add(T0, T0.AddMinutes(30), Break);

        var state = sitting.Read(T0.AddMinutes(31), Limit, Break);
        Assert.Equal(0, state.RemainingMs);
        Assert.Equal(T0.AddMinutes(45), state.BreakEndsAt);
    }

    [Fact]
    public void PlayingOnAfterTheLimit_MovesTheBreak()
    {
        var sitting = new Sitting();
        sitting.Add(T0, T0.AddMinutes(30), Break);
        sitting.Add(T0.AddMinutes(30), T0.AddMinutes(32), Break);

        Assert.Equal(T0.AddMinutes(47), sitting.Read(T0.AddMinutes(32), Limit, Break).BreakEndsAt);
    }

    [Fact]
    public void AfterTheBreak_ReadsAsANewSitting()
    {
        var sitting = new Sitting();
        sitting.Add(T0, T0.AddMinutes(30), Break);

        var state = sitting.Read(T0.AddMinutes(45), Limit, Break);
        Assert.Equal(0, state.UsedMs);
        Assert.Null(state.BreakEndsAt);
    }

    [Fact]
    public void NoLimit_NeverReached()
    {
        var sitting = new Sitting();
        sitting.Add(T0, T0.AddHours(3), Break);

        var state = sitting.Read(T0.AddHours(3), null, Break);
        Assert.Null(state.RemainingMs);
        Assert.Null(state.BreakEndsAt);
    }
}
