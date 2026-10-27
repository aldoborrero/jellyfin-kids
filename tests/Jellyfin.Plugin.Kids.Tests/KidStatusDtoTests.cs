using System;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Api;
using Jellyfin.Plugin.Kids.Enforcement;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class KidStatusDtoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 17, 30, 0, TimeSpan.Zero);
    private static readonly KidStatus Status = StatusCalculator.Calculate(new DateOnly(2026, 10, 19), 2_100_000, 60, 0, Now, Now.AddHours(4.5));

    [Fact]
    public void WithoutEnforcement_AddsNothing()
    {
        var dto = KidStatusDto.From(Status);

        Assert.False(dto.Enforced);
        Assert.Null(dto.DeniedReason);
        Assert.Null(dto.SittingLimitSeconds);
        Assert.Null(dto.BonusId);
        Assert.Null(dto.BonusUndoUntil);
    }

    [Fact]
    public void Denied_DuringTheBreak_ReportsTheSitting()
    {
        var breakEnds = Now.AddSeconds(850);
        var view = new EnforcementView(
            true,
            new Denial(DenialReason.SessionLimit, breakEnds),
            new SittingState(1_802_000, 0, breakEnds, Now.AddMinutes(-31)),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(15));

        var dto = KidStatusDto.From(Status, view);

        Assert.True(dto.Enforced);
        Assert.Equal("sessionLimit", dto.DeniedReason);
        Assert.Equal(breakEnds.UtcDateTime, dto.DeniedUntil);
        Assert.Equal(1800, dto.SittingLimitSeconds);
        Assert.Equal(1802, dto.SittingUsedSeconds);
        Assert.Equal(0, dto.SittingRemainingSeconds);
        Assert.Equal(850, dto.BreakRemainingSeconds);
        Assert.Equal(900, dto.BreakSeconds);
    }

    [Fact]
    public void Enforced_NoSittingLimit_LeavesTheSittingOut()
    {
        var dto = KidStatusDto.From(Status, new EnforcementView(true, null, null, null, TimeSpan.FromMinutes(15)));

        Assert.True(dto.Enforced);
        Assert.Null(dto.DeniedReason);
        Assert.Null(dto.SittingUsedSeconds);
        Assert.Null(dto.BreakSeconds);
    }
}
