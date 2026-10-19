using System;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Kids.Enforcement;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class ScheduleClockTests
{
    // Europe/Madrid, CEST (+02:00) in October before the change
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    // Monday 2026-10-19, 19:50 local
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 17, 50, 0, TimeSpan.Zero);

    [Fact]
    public void NoSchedules_AllowEverything()
    {
        Assert.True(ScheduleClock.IsAllowedAt([], Now, Zone));
        Assert.Null(ScheduleClock.ClosesWithin([], Now, Zone, TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData(DynamicDayOfWeek.Monday, 8, 20, true)]
    [InlineData(DynamicDayOfWeek.Tuesday, 8, 20, false)]
    [InlineData(DynamicDayOfWeek.Weekday, 8, 20, true)]
    [InlineData(DynamicDayOfWeek.Weekend, 8, 20, false)]
    [InlineData(DynamicDayOfWeek.Everyday, 8, 19.5, false)]
    [InlineData(DynamicDayOfWeek.Everyday, 19.8, 19.85, true)]
    public void IsAllowedAt_FollowsJellyfinsRule(DynamicDayOfWeek day, double start, double end, bool allowed)
    {
        Assert.Equal(allowed, ScheduleClock.IsAllowedAt([Schedule(day, start, end)], Now, Zone));
    }

    [Fact]
    public void EndHourIsInclusive()
    {
        // 19:50 is 19.8333 h; a window ending exactly then still allows it
        Assert.True(ScheduleClock.IsAllowedAt([Schedule(DynamicDayOfWeek.Monday, 0, 19 + (50 / 60.0))], Now, Zone));
    }

    [Fact]
    public void ClosesWithin_FindsTheBoundaryToTheSecond()
    {
        // Allowed until 20:00 local
        var closes = ScheduleClock.ClosesWithin([Schedule(DynamicDayOfWeek.Monday, 8, 20)], Now, Zone, TimeSpan.FromMinutes(15));

        Assert.NotNull(closes);
        Assert.InRange((closes.Value - Now.AddMinutes(10)).TotalSeconds, 0, 1.01);
    }

    [Fact]
    public void ClosesWithin_NullWhenItClosesLater()
    {
        Assert.Null(ScheduleClock.ClosesWithin([Schedule(DynamicDayOfWeek.Monday, 8, 20)], Now, Zone, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ClosesWithin_NullWhenAlreadyClosed()
    {
        Assert.Null(ScheduleClock.ClosesWithin([Schedule(DynamicDayOfWeek.Monday, 8, 19)], Now, Zone, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void AdjacentWindows_DoNotClose()
    {
        Assert.Null(ScheduleClock.ClosesWithin(
            [Schedule(DynamicDayOfWeek.Monday, 8, 20), Schedule(DynamicDayOfWeek.Monday, 20, 22)], Now, Zone, TimeSpan.FromMinutes(15)));
    }

    private static AccessSchedule Schedule(DynamicDayOfWeek day, double start, double end) => new(day, start, end, Guid.NewGuid());
}
