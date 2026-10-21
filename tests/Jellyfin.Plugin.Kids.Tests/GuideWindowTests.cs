using System;
using Jellyfin.Plugin.Kids.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class GuideWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 17, 5, 0, TimeSpan.Zero);

    [Fact]
    public void NoWindow_IsNowForTheNext24Hours()
    {
        var window = GuideWindow.Resolve(null, null, Now)!;

        Assert.Equal(Now.UtcDateTime, window.Start);
        Assert.Equal(Now.UtcDateTime.AddHours(24), window.End);
        Assert.Equal(DateTimeKind.Utc, window.Start.Kind);
    }

    [Fact]
    public void OnlyAStart_Lasts24Hours()
    {
        var start = new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc);

        var window = GuideWindow.Resolve(start, null, Now)!;

        Assert.Equal(start.AddHours(24), window.End);
    }

    [Fact]
    public void LongerThanSevenDays_IsCut()
    {
        var window = GuideWindow.Resolve(Now.UtcDateTime, Now.UtcDateTime.AddDays(30), Now)!;

        Assert.Equal(Now.UtcDateTime.AddDays(7), window.End);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void EndNotAfterStart_IsRefused(int minutes)
        => Assert.Null(GuideWindow.Resolve(Now.UtcDateTime, Now.UtcDateTime.AddMinutes(minutes), Now));

    [Fact]
    public void ValuesWithoutAnOffset_AreUtc()
    {
        var unspecified = new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Unspecified);

        var window = GuideWindow.Resolve(unspecified, null, Now)!;

        Assert.Equal(new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc), window.Start);
        Assert.Equal(DateTimeKind.Utc, window.Start.Kind);
    }

    [Fact]
    public void LocalTimes_AreConvertedToUtc()
    {
        var local = new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Local);

        var window = GuideWindow.Resolve(local, null, Now)!;

        Assert.Equal(local.ToUniversalTime(), window.Start);
        Assert.Equal(DateTimeKind.Utc, window.Start.Kind);
    }

    [Fact]
    public void AStartNearTheEndOfTime_DoesNotOverflow()
    {
        var start = new DateTime(9999, 12, 31, 12, 0, 0, DateTimeKind.Utc);

        var window = GuideWindow.Resolve(start, null, Now)!;

        Assert.Equal(DateTime.MaxValue, window.End);
    }
}
