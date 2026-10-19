using System;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Configuration;
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
        var settings = new PluginKidsSettings(() => new PluginConfiguration { DayStartsAtHour = stored });

        Assert.Equal(expected, settings.DayStartsAtHour);
    }

    [Fact]
    public void ReadsTheSettingOnEveryAccess()
    {
        var configuration = new PluginConfiguration { DayStartsAtHour = 2 };
        var settings = new PluginKidsSettings(() => configuration);
        Assert.Equal(2, settings.DayStartsAtHour);
        Assert.False(settings.EnforcementEnabled);

        configuration.DayStartsAtHour = 5;
        configuration.EnforcementEnabled = true;

        Assert.Equal(5, settings.DayStartsAtHour);
        Assert.True(settings.EnforcementEnabled);
    }

    [Fact]
    public void NoConfiguration_ReadsDefaults()
    {
        var settings = new PluginKidsSettings(() => null);

        Assert.Equal(0, settings.DayStartsAtHour);
        Assert.Equal(TimeZoneInfo.Local, settings.Zone);
        Assert.False(settings.EnforcementEnabled);
        Assert.Equal(TimeSpan.FromMinutes(15), settings.SittingBreak);
    }

    [Fact]
    public void NewConfiguration_EnforcementIsOff()
    {
        var configuration = new PluginConfiguration();

        Assert.False(configuration.EnforcementEnabled);
        Assert.Equal(15, configuration.SittingBreakMinutes);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(240, 240)]
    [InlineData(30, 30)]
    [InlineData(0, 15)]
    [InlineData(241, 15)]
    [InlineData(-5, 15)]
    public void SittingBreak_OutOfRangeReadsAsFifteen(int stored, int expectedMinutes)
    {
        var settings = new PluginKidsSettings(() => new PluginConfiguration { SittingBreakMinutes = stored });

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), settings.SittingBreak);
    }
}
