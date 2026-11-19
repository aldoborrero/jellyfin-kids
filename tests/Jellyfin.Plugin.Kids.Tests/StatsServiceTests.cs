using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Stats;
using Jellyfin.Plugin.Kids.Storage;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class StatsServiceTests : IDisposable
{
    private static readonly Guid _bluey = Guid.NewGuid();
    private static readonly Guid _duggee = Guid.NewGuid();

    private readonly StatsFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static WatchItem Episode(Guid series, string seriesName, int episode)
        => new(Guid.NewGuid(), "Episode", "E" + episode, series, seriesName, 1, episode);

    private DateTimeOffset DaysAgo(int days, int hour = 17) => new DateTimeOffset(2026, 11, 19, hour, 0, 0, TimeSpan.Zero).AddDays(-days);

    [Fact]
    public async Task NotAKid_IsNull()
    {
        Assert.Null(await _f.Service.GetAsync(Guid.NewGuid(), 7, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Days_OneEntryPerDay_OldestFirst_TodayLast_WithZeros()
    {
        var kid = await _f.MarkAsync(60);
        await _f.UsageStore.AddUsageAsync(new Dictionary<(Guid UserId, string Day), long> { [(kid, "2026-11-17")] = (42 * 60_000) + 59_000 });
        await _f.UsageStore.AddBonusAsync(kid, "2026-11-17", 15, "admin", _f.Time.Now);
        _f.Usage.AddPlayed(kid, DaysAgo(0), DaysAgo(0).AddMinutes(10));

        var stats = (await _f.Service.GetAsync(kid, 7, TestContext.Current.CancellationToken))!;

        Assert.Equal("2026-11-19", stats.Today);
        Assert.Equal(["2026-11-13", "2026-11-14", "2026-11-15", "2026-11-16", "2026-11-17", "2026-11-18", "2026-11-19"], stats.Days.Select(d => d.Day));
        Assert.Equal([0, 0, 0, 0, 42, 0, 10], stats.Days.Select(d => d.UsedMinutes));
        Assert.Equal([0, 0, 0, 0, 15, 0, 0], stats.Days.Select(d => d.BonusMinutes));
        Assert.Equal(90, stats.RetentionDays);
    }

    [Fact]
    public async Task AllowanceMinutes_IsTheDaysNotedAllowance_OrTodays()
    {
        var kid = await _f.MarkAsync(30);

        // Yesterday played with 30 a day; then the parent raises it to 60 and the kid plays today
        _f.Time.Now = DaysAgo(1, 18);
        _f.Usage.AddPlayed(kid, DaysAgo(1), DaysAgo(1).AddMinutes(5));
        await _f.Usage.FlushAsync(TestContext.Current.CancellationToken);
        _f.Time.Now = DaysAgo(0, 18);
        var profile = (await _f.Profiles.GetAsync(kid, TestContext.Current.CancellationToken))!;
        Assert.Equal(SaveOutcome.Saved, (await _f.Profiles.SaveAsync(kid, profile.Revision, new KidProfileInput { DailyAllowanceMinutes = 60 })).Outcome);

        var stats = (await _f.Service.GetAsync(kid, 3, TestContext.Current.CancellationToken))!;

        // Two days ago nothing was noted: today's allowance
        Assert.Equal<int?>([60, 30, 60], stats.Days.Select(d => d.AllowanceMinutes));
    }

    [Fact]
    public async Task AllowanceMinutes_NullIsUnlimited()
    {
        var kid = await _f.MarkAsync(null);
        _f.Usage.AddPlayed(kid, DaysAgo(0), DaysAgo(0).AddMinutes(1));

        var stats = (await _f.Service.GetAsync(kid, 1, TestContext.Current.CancellationToken))!;

        Assert.Null(Assert.Single(stats.Days).AllowanceMinutes);
    }

    [Fact]
    public async Task Off_TopSeriesAndBreaksAreEmpty_EvenWithKeptData()
    {
        var kid = await _f.MarkAsync(60);
        _f.Settings.AdvancedStats = true;
        _f.Play(kid, Episode(_bluey, "Bluey", 1), DaysAgo(0), 600);
        _f.Watch.OnBreak(new BreakNotice(kid, DaysAgo(0), TimeSpan.FromMinutes(15), false));
        await _f.Watch.FlushAsync(TestContext.Current.CancellationToken);
        _f.Settings.AdvancedStats = false;

        var stats = (await _f.Service.GetAsync(kid, 7, TestContext.Current.CancellationToken))!;

        Assert.False(stats.AdvancedStats);
        Assert.Empty(stats.TopSeries);
        Assert.Empty(stats.Breaks);
        Assert.Equal(10, stats.Days[^1].UsedMinutes);

        // Kept, and still shown on the dashboard
        var detail = (await _f.Service.GetDetailAsync(kid, 7, TestContext.Current.CancellationToken))!;
        Assert.Single(detail.Series);
    }

    [Fact]
    public async Task TopSeries_GroupsEpisodesMoviesAndChannels_AtMostFive()
    {
        var kid = await _f.MarkAsync(null);
        _f.Settings.AdvancedStats = true;
        var channel = Guid.NewGuid();
        _f.Play(kid, Episode(_bluey, "Bluey", 1), DaysAgo(2), 1200, play: "a");
        _f.Play(kid, Episode(_bluey, "Bluey", 2), DaysAgo(1), 1200, play: "b");
        _f.Play(kid, Episode(_duggee, "Hey Duggee", 1), DaysAgo(1, 19), 900, play: "c");
        var movie = new WatchItem(Guid.NewGuid(), "Movie", "Coco");
        _f.Play(kid, movie, DaysAgo(0, 10), 1800, play: "d");
        _f.Play(kid, new WatchItem(channel, "TvChannel", "PekeTV", ChannelId: channel, ChannelName: "PekeTV"), DaysAgo(0, 12), 600, play: "e");
        foreach (var (name, i) in new[] { "A", "B", "C" }.Select((n, i) => (n, i)))
        {
            _f.Play(kid, new WatchItem(Guid.NewGuid(), "Movie", name), DaysAgo(3, 8 + i), 60, play: name);
        }

        var stats = (await _f.Service.GetAsync(kid, 7, TestContext.Current.CancellationToken))!;

        Assert.Equal(5, stats.TopSeries.Count);
        Assert.Equal(new SeriesStats(_bluey, "Bluey", 40, 2, false), stats.TopSeries[0]);
        Assert.Equal(new SeriesStats(movie.ItemId, "Coco", 30, 1, false), stats.TopSeries[1]);
        Assert.Equal(new SeriesStats(_duggee, "Hey Duggee", 15, 1, false), stats.TopSeries[2]);
        Assert.Equal(new SeriesStats(channel, "PekeTV", 10, 1, true), stats.TopSeries[3]);
    }

    [Fact]
    public async Task Breaks_PerDay_WhenOn()
    {
        var kid = await _f.MarkAsync(null);
        _f.Settings.AdvancedStats = true;
        _f.Watch.OnBreak(new BreakNotice(kid, DaysAgo(0, 10), TimeSpan.FromMinutes(15), false));
        _f.Watch.OnBreak(new BreakNotice(kid, DaysAgo(0, 16), TimeSpan.FromMinutes(6), true));
        _f.Watch.OnBreak(new BreakNotice(kid, DaysAgo(2, 16), TimeSpan.FromMinutes(15), false));

        var stats = (await _f.Service.GetAsync(kid, 7, TestContext.Current.CancellationToken))!;

        Assert.Equal([new BreakDay("2026-11-17", 1, 15), new BreakDay("2026-11-19", 2, 21)], stats.Breaks);
    }

    [Fact]
    public async Task Detail_HasDaysBySeries_EpisodesHoursAndDevices()
    {
        var kid = await _f.MarkAsync(60);
        _f.Settings.AdvancedStats = true;
        _f.Play(kid, Episode(_bluey, "Bluey", 1), DaysAgo(0, 17), 600, play: "a");
        _f.Play(kid, Episode(_bluey, "Bluey", 2), DaysAgo(0, 18), 300, session: "s2", play: "b", device: "Tablet");

        var detail = (await _f.Service.GetDetailAsync(kid, 14, TestContext.Current.CancellationToken))!;

        Assert.Equal(14, detail.Days.Count);
        var today = detail.Days[^1];
        Assert.Equal(900, today.UsedSeconds);
        Assert.Equal(900, Assert.Single(today.Series).Seconds);
        var bluey = Assert.Single(detail.Series);
        Assert.Equal(["E1", "E2"], bluey.Episodes.Select(e => e.Name));
        Assert.Equal(600, detail.Hours.Weekdays[17]);
        Assert.Equal(300, detail.Hours.Weekdays[18]);
        Assert.Equal(["Salón TV", "Tablet"], detail.Devices.Select(d => d.Device));
    }

    [Fact]
    public async Task Prune_UsesTheRetentionSetting_ForUsageAndTheWatchLog()
    {
        var kid = await _f.MarkAsync(null);
        _f.Settings.AdvancedStats = true;
        _f.Play(kid, Episode(_bluey, "Bluey", 1), DaysAgo(31), 60, play: "old");
        _f.Play(kid, Episode(_bluey, "Bluey", 2), DaysAgo(30), 60, play: "kept");
        _f.Watch.OnBreak(new BreakNotice(kid, DaysAgo(31), TimeSpan.FromMinutes(15), false));
        await _f.Usage.FlushAsync(TestContext.Current.CancellationToken);
        await _f.Watch.FlushAsync(TestContext.Current.CancellationToken);
        _f.Settings.RetentionDays = 30;

        Assert.True(await _f.Usage.PruneAsync(TestContext.Current.CancellationToken) >= 3);

        var detail = (await _f.Service.GetDetailAsync(kid, 60, TestContext.Current.CancellationToken))!;
        Assert.Equal(60, detail.Days.Single(d => d.Day == "2026-10-20").UsedSeconds);
        Assert.Equal(0, detail.Days.Single(d => d.Day == "2026-10-19").UsedSeconds);
        Assert.Equal(["E2"], detail.Series.Single().Episodes.Select(e => e.Name));
        Assert.Empty(detail.Breaks);
    }

    [Fact]
    public async Task Prune_KeepsToday_WhateverTheSetting()
    {
        var kid = await _f.MarkAsync(null);
        _f.Usage.AddPlayed(kid, DaysAgo(0), DaysAgo(0).AddMinutes(1));
        await _f.Usage.FlushAsync(TestContext.Current.CancellationToken);
        _f.Settings.RetentionDays = 0;

        await _f.Usage.PruneAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, (await _f.Service.GetAsync(kid, 1, TestContext.Current.CancellationToken))!.Days[0].UsedMinutes);
    }
}
