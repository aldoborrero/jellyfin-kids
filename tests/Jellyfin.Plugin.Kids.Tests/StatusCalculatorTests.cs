using System;
using Jellyfin.Plugin.Kids.Allowance;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class StatusCalculatorTests
{
    private static readonly DateOnly _day = new(2026, 10, 18);
    private static readonly DateTimeOffset _now = new(2026, 10, 18, 16, 2, 11, TimeSpan.Zero);
    private static readonly DateTimeOffset _next = new(2026, 10, 18, 22, 0, 0, TimeSpan.Zero);

    private static KidStatus Calculate(long usedMs, int? allowanceMinutes, int bonusMinutes = 0)
        => StatusCalculator.Calculate(_day, usedMs, allowanceMinutes, bonusMinutes, _now, _next);

    private static long Minutes(int minutes) => minutes * 60_000L;

    [Fact]
    public void Unlimited_HasNoAllowanceNoRemainingAndIsNeverUp()
    {
        var status = Calculate(Minutes(600), null);

        Assert.Null(status.AllowanceSeconds);
        Assert.Null(status.RemainingSeconds);
        Assert.False(status.TimeUp);
        Assert.Equal(36_000, status.UsedSeconds);
    }

    [Fact]
    public void Under_HasRemaining()
    {
        var status = Calculate(Minutes(25), 30);

        Assert.Equal(1800, status.AllowanceSeconds);
        Assert.Equal(300, status.RemainingSeconds);
        Assert.False(status.TimeUp);
    }

    [Fact]
    public void At_IsUp()
    {
        var status = Calculate(Minutes(30), 30);

        Assert.Equal(0, status.RemainingSeconds);
        Assert.True(status.TimeUp);
    }

    [Fact]
    public void Over_RemainingIsNeverNegative()
    {
        var status = Calculate(Minutes(50), 30, 10);

        Assert.Equal(0, status.RemainingSeconds);
        Assert.True(status.TimeUp);
    }

    [Fact]
    public void ZeroAllowance_IsUpUntilABonus()
    {
        Assert.True(Calculate(0, 0).TimeUp);

        var withBonus = Calculate(0, 0, 15);

        Assert.False(withBonus.TimeUp);
        Assert.Equal(900, withBonus.RemainingSeconds);
        Assert.Equal(900, withBonus.BonusSeconds);
    }

    [Fact]
    public void Bonus_AddsToTheAllowance()
    {
        var status = Calculate(Minutes(35), 30, 10);

        Assert.Equal(600, status.BonusSeconds);
        Assert.Equal(300, status.RemainingSeconds);
        Assert.False(status.TimeUp);
    }

    [Fact]
    public void UsedSeconds_RoundsDown()
    {
        Assert.Equal(59, Calculate(59_999, null).UsedSeconds);
    }

    [Fact]
    public void CarriesTheDayTimesAndWarnings()
    {
        var status = Calculate(0, null);

        Assert.Equal(_day, status.Date);
        Assert.Equal(_now, status.ServerTime);
        Assert.Equal(_next, status.NextDayStartsAt);
        Assert.Equal([300, 60], status.WarnAtSeconds);
    }
}
