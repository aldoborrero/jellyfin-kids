using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// A kid's statistics over the last days (feature 22): screen time per day from the daily usage, always; what was
/// watched and the breaks from the watch log, only while advanced statistics are on. Access rules are the caller's job.
/// </summary>
public sealed class StatsService
{
    /// <summary>The most days the client contract's range covers.</summary>
    public const int MaxClientDays = 90;

    /// <summary>The most days the dashboard's range covers (the longest retention).</summary>
    public const int MaxDetailDays = 365;

    private readonly StatsStore _store;
    private readonly UsageService _usage;
    private readonly WatchLog _watch;
    private readonly KidProfileService _profiles;
    private readonly ILiveTvChannelCatalog _channels;
    private readonly IKidsSettings _settings;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatsService"/> class.
    /// </summary>
    /// <param name="store">The statistics store.</param>
    /// <param name="usage">The screen-time service, flushed first so today is up to date.</param>
    /// <param name="watch">The watch log, flushed first for the same reason.</param>
    /// <param name="profiles">The kid profiles, for who is a kid and today's allowance.</param>
    /// <param name="channels">The live TV channels, for the name of a channel a programme was reported for.</param>
    /// <param name="settings">The household settings.</param>
    /// <param name="time">The clock.</param>
    public StatsService(StatsStore store, UsageService usage, WatchLog watch, KidProfileService profiles, ILiveTvChannelCatalog channels, IKidsSettings settings, TimeProvider time)
    {
        _channels = channels;
        _store = store;
        _usage = usage;
        _watch = watch;
        _profiles = profiles;
        _settings = settings;
        _time = time;
    }

    /// <summary>
    /// Gets a kid's statistics for clients (<c>GET /Kids/Users/{userId}/Stats</c>).
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="days">How many days, today included: 1–<see cref="MaxClientDays"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The statistics, or null if the user is not a kid.</returns>
    /// <exception cref="InvalidOperationException">The store cannot be read; never reported as 0 used.</exception>
    public async Task<KidStats?> GetAsync(Guid userId, int days, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, MaxDetailDays);
        var range = await LoadAsync(userId, days, cancellationToken).ConfigureAwait(false);
        if (range is null)
        {
            return null;
        }

        var (today, from, allowance, stored, advanced) = range.Value;
        var list = Days(from, today, stored, allowance)
            .Select(d => new DayStats(d.Day, (int)(d.Stored.UsedMs / 60_000), d.Stored.BonusMinutes, d.Allowance))
            .ToList();
        IReadOnlyList<SeriesStats> top = [];
        IReadOnlyList<BreakDay> breaks = [];
        if (advanced)
        {
            var segments = await ReadSegmentsAsync(userId, DayClock.Format(from), DayClock.Format(today), cancellationToken).ConfigureAwait(false);
            top = StatsCalculator.TopSeries(segments);
            breaks = StatsCalculator.Breaks(await _store.ReadBreaksAsync(userId, DayClock.Format(from), DayClock.Format(today), cancellationToken).ConfigureAwait(false));
        }

        return new KidStats(userId, DayClock.Format(today), advanced, _settings.RetentionDays, list, top, breaks);
    }

    /// <summary>
    /// Gets a kid's statistics for the dashboard: per day with series, per series with episodes, per hour and per
    /// device. The watch log is read even while advanced statistics are off, so what was kept can still be looked at.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="days">How many days, today included: 1–<see cref="MaxDetailDays"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The statistics, or null if the user is not a kid.</returns>
    public async Task<KidStatsDetail?> GetDetailAsync(Guid userId, int days, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, MaxDetailDays);
        var range = await LoadAsync(userId, days, cancellationToken).ConfigureAwait(false);
        if (range is null)
        {
            return null;
        }

        var (today, from, allowance, stored, advanced) = range.Value;
        var fromKey = DayClock.Format(from);
        var toKey = DayClock.Format(today);
        var segments = await ReadSegmentsAsync(userId, fromKey, toKey, cancellationToken).ConfigureAwait(false);
        var breaks = await _store.ReadBreaksAsync(userId, fromKey, toKey, cancellationToken).ConfigureAwait(false);
        var perDay = StatsCalculator.PerDay(segments);
        var list = Days(from, today, stored, allowance)
            .Select(d => new DetailDay(
                d.Day,
                (int)(d.Stored.UsedMs / 1000),
                d.Stored.BonusMinutes,
                d.Allowance,
                perDay.TryGetValue(d.Day, out var series) ? series : []))
            .ToList();
        return new KidStatsDetail(
            userId,
            toKey,
            advanced,
            _settings.RetentionDays,
            list,
            StatsCalculator.Series(segments),
            StatsCalculator.Hours(segments, _settings.Zone),
            StatsCalculator.Devices(segments),
            StatsCalculator.Breaks(breaks));
    }

    private static IEnumerable<(string Day, StoredDay Stored, int? Allowance)> Days(DateOnly from, DateOnly today, IReadOnlyDictionary<string, StoredDay> stored, int? todaysAllowance)
    {
        for (var day = from; day <= today; day = day.AddDays(1))
        {
            var key = DayClock.Format(day);
            var found = stored.TryGetValue(key, out var row) ? row : new StoredDay(0, 0, false, null);

            // Today always follows the profile as it is now; an earlier day without a note takes today's
            var allowance = key != DayClock.Format(today) && found.AllowanceKnown ? found.AllowanceMinutes : todaysAllowance;
            yield return (key, found, allowance);
        }
    }

    private async Task<(DateOnly Today, DateOnly From, int? Allowance, IReadOnlyDictionary<string, StoredDay> Stored, bool Advanced)?> LoadAsync(
        Guid userId,
        int days,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        // Counted time waiting in memory goes to the store first, so today matches the Status
        await _usage.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _watch.FlushAsync(cancellationToken).ConfigureAwait(false);
        var today = _usage.CurrentDayClock().DayOf(_time.GetUtcNow());
        var from = today.AddDays(1 - days);
        var stored = await _store.ReadDaysAsync(userId, DayClock.Format(from), DayClock.Format(today), cancellationToken).ConfigureAwait(false);
        return (today, from, profile.Settings.DailyAllowanceMinutes, stored, _settings.AdvancedStats);
    }

    // A client may report the live programme instead of the channel: the event handler cannot look the channel up
    // (no I/O there), so its name is filled in here. A channel that no longer exists keeps the programme's name.
    private async Task<IReadOnlyList<WatchSegment>> ReadSegmentsAsync(Guid userId, string fromDay, string toDay, CancellationToken cancellationToken)
    {
        var segments = await _store.ReadSegmentsAsync(userId, fromDay, toDay, cancellationToken).ConfigureAwait(false);
        var unnamed = segments
            .Where(s => s.Item.ChannelId is not null && s.Item.ChannelName is null)
            .Select(s => s.Item.ChannelId!.Value)
            .ToHashSet();
        if (unnamed.Count == 0)
        {
            return segments;
        }

        var names = _channels.Resolve(unnamed).ToDictionary(c => c.Id, c => c.Name);
        return [.. segments.Select(s => s.Item.ChannelId is { } id && s.Item.ChannelName is null && names.TryGetValue(id, out var name)
            ? s with { Item = s.Item with { ChannelName = name } }
            : s)];
    }
}
