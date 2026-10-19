using System;
using Jellyfin.Plugin.Kids.Enforcement;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class KidBudgetTests
{
    private static readonly DateOnly Day = new(2026, 10, 19);

    [Fact]
    public void TimeUp_WhenUsedReachesAllowancePlusBonus()
    {
        var budget = new KidBudget(Day, 59 * 60_000, allowanceMinutes: 50, bonusMinutes: 10);
        Assert.False(budget.TimeUp);
        Assert.Equal(60_000, budget.RemainingMs);

        budget.Add(Day, 60_000);

        Assert.True(budget.TimeUp);
        Assert.Equal(0, budget.RemainingMs);
    }

    [Fact]
    public void Unlimited_IsNeverTimeUp()
    {
        var budget = new KidBudget(Day, 0, allowanceMinutes: null, bonusMinutes: 0);
        budget.Add(Day, 86_400_000);

        Assert.False(budget.TimeUp);
        Assert.Null(budget.LimitMs);
        Assert.Null(budget.RemainingMs);
    }

    [Fact]
    public void ZeroAllowance_IsTimeUpAtOnce()
    {
        Assert.True(new KidBudget(Day, 0, 0, 0).TimeUp);
    }

    [Fact]
    public void NewDay_StartsAfreshWithoutBonus()
    {
        var budget = new KidBudget(Day, 60 * 60_000, allowanceMinutes: 30, bonusMinutes: 30);
        Assert.True(budget.TimeUp);

        var rolled = budget.Add(Day.AddDays(1), 1_000);

        Assert.True(rolled);
        Assert.Equal(Day.AddDays(1), budget.Day);
        Assert.Equal(1_000, budget.UsedMs);
        Assert.Equal(0, budget.BonusMinutes);
        Assert.False(budget.TimeUp);
    }

    [Fact]
    public void EarlierDay_IsIgnored()
    {
        var budget = new KidBudget(Day, 1_000, 30, 0);

        Assert.False(budget.Add(Day.AddDays(-1), 5_000));
        Assert.Equal(1_000, budget.UsedMs);
    }
}
