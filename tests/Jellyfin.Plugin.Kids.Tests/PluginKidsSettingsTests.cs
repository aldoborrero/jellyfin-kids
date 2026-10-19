using System;
using Jellyfin.Plugin.Kids.Allowance;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class PluginKidsSettingsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(23, 23)]
    [InlineData(-1, 0)]
    [InlineData(24, 0)]
    public void DayStartsAtHour_OutOfRangeReadsAsZero(int stored, int expected)
    {
        var settings = new PluginKidsSettings(() => stored);

        Assert.Equal(expected, settings.DayStartsAtHour);
    }

    [Fact]
    public void ReadsTheSettingOnEveryAccess()
    {
        var stored = 2;
        var settings = new PluginKidsSettings(() => stored);
        Assert.Equal(2, settings.DayStartsAtHour);

        stored = 5;

        Assert.Equal(5, settings.DayStartsAtHour);
    }

    [Fact]
    public void NoConfiguration_ReadsMidnightInLocalTime()
    {
        var settings = new PluginKidsSettings(() => null);

        Assert.Equal(0, settings.DayStartsAtHour);
        Assert.Equal(TimeZoneInfo.Local, settings.Zone);
    }
}
