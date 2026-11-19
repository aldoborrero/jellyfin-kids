using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// Turns watch segments and breaks into the statistics views (feature 22). Pure.
/// </summary>
/// <remarks>
/// Grouping: an episode goes with its series; a movie or other item is its own entry (keyed by the item); live TV is
/// grouped per channel. Plays count playbacks, not segments, so a pause that split a playback is still one play.
/// </remarks>
public static class StatsCalculator
{
    /// <summary>How many entries <c>TopSeries</c> holds at most.</summary>
    public const int TopSeriesCount = 5;

    /// <summary>
    /// The most watched entries over the segments, by time, for the client contract.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <param name="max">How many at most.</param>
    /// <returns>The entries, most watched first.</returns>
    public static IReadOnlyList<SeriesStats> TopSeries(IEnumerable<WatchSegment> segments, int max = TopSeriesCount)
        => Series(segments)
            .Take(max)
            .Select(s => new SeriesStats(s.SeriesId, s.Name, RoundMinutes(s.Seconds), s.Plays, s.IsLiveTv))
            .ToList();

    /// <summary>
    /// Every entry watched, by time, with the episodes of each series.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <returns>The entries, most watched first (ties by name).</returns>
    public static IReadOnlyList<SeriesDetail> Series(IEnumerable<WatchSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments
            .GroupBy(s => Group(s.Item))
            .Select(g =>
            {
                var episodes = g.Key.IsSeries
                    ? g.GroupBy(s => s.Item.ItemId?.ToString("N", CultureInfo.InvariantCulture) ?? "name:" + s.Item.ItemName)
                        .Select(e => new EpisodeStats(
                            e.First().Item.ItemId,
                            e.Last().Item.ItemName ?? string.Empty,
                            e.First().Item.Season,
                            e.First().Item.Episode,
                            e.Sum(s => s.Seconds),
                            Plays(e),
                            e.Max(s => s.EndedAt).UtcDateTime))
                        .OrderByDescending(e => e.Seconds)
                        .ThenBy(e => e.Season)
                        .ThenBy(e => e.Episode)
                        .ToList()
                    : [];
                return new SeriesDetail(
                    g.Key.Key,
                    g.Key.Id,
                    NameOf(g),
                    g.Key.IsLiveTv,
                    g.Sum(s => s.Seconds),
                    Plays(g),
                    g.Max(s => s.EndedAt).UtcDateTime,
                    episodes);
            })
            .OrderByDescending(s => s.Seconds)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// What each day's segments played, by entry.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <returns>Per day, the entries most watched first.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<DaySeries>> PerDay(IEnumerable<WatchSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments
            .GroupBy(s => s.Day, StringComparer.Ordinal)
            .ToDictionary(
                d => d.Key,
                d => (IReadOnlyList<DaySeries>)d.GroupBy(s => Group(s.Item))
                    .Select(g => new DaySeries(g.Key.Key, NameOf(g), g.Key.IsLiveTv, g.Sum(s => s.Seconds)))
                    .OrderByDescending(s => s.Seconds)
                    .ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// When the segments played, per hour of the day in a time zone: each segment's seconds spread evenly over its
    /// start to end, weekdays and weekends apart.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <param name="zone">The server's time zone.</param>
    /// <returns>The grid.</returns>
    public static HourGrid Hours(IEnumerable<WatchSegment> segments, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(zone);
        var weekdays = new double[24];
        var weekend = new double[24];
        foreach (var segment in segments.Where(s => s.Seconds > 0))
        {
            var span = (segment.EndedAt - segment.StartedAt).TotalSeconds;
            if (span <= 0)
            {
                Bucket(TimeZoneInfo.ConvertTime(segment.StartedAt, zone), weekdays, weekend, segment.Seconds);
                continue;
            }

            var cursor = segment.StartedAt;
            while (cursor < segment.EndedAt)
            {
                var local = TimeZoneInfo.ConvertTime(cursor, zone);
                var hourStart = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset);
                var next = hourStart.AddHours(1);
                if (next <= cursor)
                {
                    next = cursor.AddHours(1);
                }

                var end = next < segment.EndedAt ? next : segment.EndedAt;
                Bucket(local, weekdays, weekend, segment.Seconds * (end - cursor).TotalSeconds / span);
                cursor = end;
            }
        }

        return new HourGrid(Round(weekdays), Round(weekend));
    }

    /// <summary>
    /// Where the segments played, per device and app.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <returns>The devices, most used first.</returns>
    public static IReadOnlyList<DeviceStats> Devices(IEnumerable<WatchSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments
            .GroupBy(s => (Device: string.IsNullOrWhiteSpace(s.Device) ? "?" : s.Device, s.Client))
            .Select(g => new DeviceStats(g.Key.Device, g.Key.Client, g.Sum(s => s.Seconds), Plays(g)))
            .OrderByDescending(d => d.Seconds)
            .ToList();
    }

    /// <summary>
    /// The breaks per day.
    /// </summary>
    /// <param name="breaks">The breaks.</param>
    /// <returns>The days with breaks, oldest first.</returns>
    public static IReadOnlyList<BreakDay> Breaks(IEnumerable<BreakRecord> breaks)
    {
        ArgumentNullException.ThrowIfNull(breaks);
        return breaks
            .GroupBy(b => b.Day, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new BreakDay(g.Key, g.Count(), g.Sum(b => b.Minutes)))
            .ToList();
    }

    private static int RoundMinutes(int seconds) => (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero);

    private static int Plays(IEnumerable<WatchSegment> segments) => segments.Select(s => s.PlaybackId).Distinct(StringComparer.Ordinal).Count();

    private static void Bucket(DateTimeOffset local, double[] weekdays, double[] weekend, double seconds)
        => (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? weekend : weekdays)[local.Hour] += seconds;

    private static int[] Round(double[] values) => [.. values.Select(v => (int)Math.Round(v, MidpointRounding.AwayFromZero))];

    // The latest name wins, so a renamed series shows its current name
    private static string NameOf(IGrouping<GroupKey, WatchSegment> group)
    {
        var last = group.MaxBy(s => s.EndedAt)!.Item;
        var name = group.Key.IsLiveTv
            ? last.ChannelName ?? last.ItemName
            : group.Key.IsSeries ? last.SeriesName ?? last.ItemName : last.ItemName;
        return string.IsNullOrWhiteSpace(name) ? "?" : name;
    }

    private static GroupKey Group(WatchItem item)
    {
        if (item.ChannelId is { } channel)
        {
            return new GroupKey("live:" + Id(channel), channel, true, false);
        }

        if (item.SeriesId is { } series)
        {
            return new GroupKey("series:" + Id(series), series, false, true);
        }

        if (!string.IsNullOrEmpty(item.SeriesName))
        {
            return new GroupKey("name:" + item.SeriesName, null, false, true);
        }

        return item.ItemId is { } id
            ? new GroupKey("item:" + Id(id), id, false, false)
            : new GroupKey("name:" + item.ItemName, null, false, false);
    }

    private static string Id(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);

    private sealed record GroupKey(string Key, Guid? Id, bool IsLiveTv, bool IsSeries);
}
