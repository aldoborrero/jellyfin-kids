using System;
using System.Linq;
using Jellyfin.Plugin.Kids.Allowance;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class DayClockTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0)
        => new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static TimeZoneInfo Madrid()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        }
        catch (TimeZoneNotFoundException)
        {
            Assert.Skip("The Europe/Madrid time zone is not installed on this machine.");
            throw;
        }
    }

    [Fact]
    public void MidnightStart_SplitsAtLocalMidnight()
    {
        var clock = new DayClock(Madrid(), 0);

        // 21:59Z is 23:59 in Madrid (CEST, +2); 22:00Z is midnight
        Assert.Equal(new DateOnly(2026, 10, 18), clock.DayOf(Utc(2026, 10, 18, 21, 59)));
        Assert.Equal(new DateOnly(2026, 10, 19), clock.DayOf(Utc(2026, 10, 18, 22, 0)));
    }

    [Fact]
    public void FourAmStart_LateNightBelongsToThePreviousDay()
    {
        var clock = new DayClock(Madrid(), 4);

        Assert.Equal(new DateOnly(2026, 10, 18), clock.DayOf(Utc(2026, 10, 19, 1, 30)));
        Assert.Equal(new DateOnly(2026, 10, 19), clock.DayOf(Utc(2026, 10, 19, 2, 0)));
    }

    [Fact]
    public void SpringForward_StartInTheGap_MovesToTheFirstValidTime()
    {
        var clock = new DayClock(Madrid(), 2);
        var day = new DateOnly(2027, 3, 28);

        // 02:00 local does not exist that night; the day starts at 03:00 CEST = 01:00Z
        Assert.Equal(Utc(2027, 3, 28, 1, 0), clock.StartOf(day));
        Assert.Equal(TimeSpan.FromHours(23), clock.StartOf(day.AddDays(1)) - clock.StartOf(day));
        Assert.Equal(day.AddDays(-1), clock.DayOf(Utc(2027, 3, 28, 0, 59)));
        Assert.Equal(day, clock.DayOf(Utc(2027, 3, 28, 1, 0)));
    }

    [Fact]
    public void FallBack_AmbiguousStart_TakesTheEarlierInstant()
    {
        var clock = new DayClock(Madrid(), 2);
        var day = new DateOnly(2026, 10, 25);

        // 02:00 local happens twice; the first one is 02:00 CEST = 00:00Z
        Assert.Equal(Utc(2026, 10, 25, 0, 0), clock.StartOf(day));
        Assert.Equal(TimeSpan.FromHours(25), clock.StartOf(day.AddDays(1)) - clock.StartOf(day));
        Assert.Equal(day, clock.DayOf(Utc(2026, 10, 25, 1, 30)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(23)]
    public void NextStart_IsTheStartOfTheNextDayAndInTheFuture(int hour)
    {
        var clock = new DayClock(Madrid(), hour);
        var instants = Enumerable.Range(0, 24 * 4 * 3).Select(i => Utc(2026, 10, 24, 0).AddMinutes(15 * i))
            .Concat(Enumerable.Range(0, 24 * 4 * 3).Select(i => Utc(2027, 3, 27, 0).AddMinutes(15 * i)));

        foreach (var utc in instants)
        {
            var next = clock.NextStart(utc);
            Assert.Equal(clock.StartOf(clock.DayOf(utc).AddDays(1)), next);
            Assert.True(next > utc, $"NextStart({utc:O}) = {next:O} is not after it");
            Assert.True(clock.StartOf(clock.DayOf(utc)) <= utc, $"StartOf(DayOf({utc:O})) is after it");
        }
    }

    [Fact]
    public void Split_AcrossTheStart_GivesTwoPartsOnTheirOwnDays()
    {
        var clock = new DayClock(TimeZoneInfo.Utc, 4);

        var parts = clock.Split(Utc(2026, 10, 18, 3, 50), Utc(2026, 10, 18, 4, 5)).ToList();

        Assert.Equal(
            [(new DateOnly(2026, 10, 17), TimeSpan.FromMinutes(10)), (new DateOnly(2026, 10, 18), TimeSpan.FromMinutes(5))],
            parts);
    }

    [Fact]
    public void Split_InsideOneDay_GivesOnePart()
    {
        var clock = new DayClock(TimeZoneInfo.Utc, 0);

        var parts = clock.Split(Utc(2026, 10, 18, 9, 0), Utc(2026, 10, 18, 9, 0).AddSeconds(30)).ToList();

        Assert.Equal([(new DateOnly(2026, 10, 18), TimeSpan.FromSeconds(30))], parts);
    }

    [Fact]
    public void Split_EmptyOrReversed_GivesNothing()
    {
        var clock = new DayClock(TimeZoneInfo.Utc, 0);
        var at = Utc(2026, 10, 18, 9, 0);

        Assert.Empty(clock.Split(at, at));
        Assert.Empty(clock.Split(at, at.AddSeconds(-5)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Constructor_HourOutOfRange_Throws(int hour)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DayClock(TimeZoneInfo.Utc, hour));
    }

    [Fact]
    public void Format_IsIsoDate()
    {
        Assert.Equal("2026-03-07", DayClock.Format(new DateOnly(2026, 3, 7)));
    }
}
