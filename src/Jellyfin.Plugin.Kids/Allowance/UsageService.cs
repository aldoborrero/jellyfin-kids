using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// Kids' screen time: counted playing time waiting to be stored, status, bonus minutes and offline reports.
/// </summary>
/// <remarks>
/// Counted time accumulates in memory per kid and day and is flushed in one write (research R5); status reads add the
/// amounts not yet stored, so clients see live numbers. A failed flush keeps them for the next one.
/// </remarks>
public sealed class UsageService : IDisposable
{
    private readonly UsageStore _store;
    private readonly KidProfileService _profiles;
    private readonly IKidsSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<UsageService> _logger;
    private readonly Lock _pendingLock = new();
    private readonly Dictionary<(Guid UserId, string Day), long> _pending = [];
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private bool _flushFailing;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsageService"/> class.
    /// </summary>
    /// <param name="store">The usage store.</param>
    /// <param name="profiles">The kid profiles, for who is a kid and their allowance.</param>
    /// <param name="settings">The household settings.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public UsageService(UsageStore store, KidProfileService profiles, IKidsSettings settings, TimeProvider time, ILogger<UsageService> logger)
    {
        _store = store;
        _profiles = profiles;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Gets how many (kid, day) amounts are waiting to be stored.
    /// </summary>
    public int PendingCount
    {
        get
        {
            lock (_pendingLock)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>
    /// Builds the day rule from the current settings, so a change applies to the next read.
    /// </summary>
    /// <returns>The day clock.</returns>
    public DayClock CurrentDayClock() => new(_settings.Zone, _settings.DayStartsAtHour);

    /// <summary>
    /// Gets a kid's status for today.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The status, or null if the user is not a kid.</returns>
    /// <exception cref="InvalidOperationException">The store cannot be read; never reported as 0 used.</exception>
    public async Task<KidStatus?> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        return await StatusAsync(userId, profile.Settings.DailyAllowanceMinutes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds playing time, split at day starts. Called from playback events: no I/O.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="from">The start of the interval.</param>
    /// <param name="to">The end of the interval.</param>
    public void AddPlayed(Guid userId, DateTimeOffset from, DateTimeOffset to)
    {
        var parts = CurrentDayClock().Split(from, to).ToList();
        lock (_pendingLock)
        {
            foreach (var (day, length) in parts)
            {
                var ms = (long)length.TotalMilliseconds;
                if (ms > 0)
                {
                    var key = (userId, DayClock.Format(day));
                    _pending[key] = _pending.GetValueOrDefault(key) + ms;
                }
            }
        }
    }

    /// <summary>
    /// Stores the counted time in one write. Time counted while the write runs stays pending for the next flush.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if everything pending was stored; <c>false</c> if the write failed and it is kept.</returns>
    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<(Guid UserId, string Day), long> snapshot;
            lock (_pendingLock)
            {
                snapshot = new Dictionary<(Guid UserId, string Day), long>(_pending);
            }

            if (snapshot.Count == 0)
            {
                return true;
            }

            try
            {
                await _store.AddUsageAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!_flushFailing)
                {
                    _flushFailing = true;
                    _logger.LogWarning(ex, "Could not store kids screen time; keeping {Count} pending amount(s)", snapshot.Count);
                }

                return false;
            }

            if (_flushFailing)
            {
                _flushFailing = false;
                _logger.LogInformation("Stored kids screen time again");
            }

            lock (_pendingLock)
            {
                foreach (var (key, written) in snapshot)
                {
                    var left = _pending.GetValueOrDefault(key) - written;
                    if (left > 0)
                    {
                        _pending[key] = left;
                    }
                    else
                    {
                        _pending.Remove(key);
                    }
                }
            }

            return true;
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// Forgets a user's counted time that is not stored yet.
    /// </summary>
    /// <param name="userId">The user.</param>
    public void DropPending(Guid userId)
    {
        lock (_pendingLock)
        {
            foreach (var key in _pending.Keys.Where(k => k.UserId == userId).ToList())
            {
                _pending.Remove(key);
            }
        }
    }

    /// <summary>
    /// Deletes all of a user's screen time, stored and pending. Serialised with flushes, so a flush in progress cannot
    /// write it back.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the user's screen time is gone.</returns>
    public async Task PurgeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DropPending(userId);
            await _store.PurgeUserAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// Deletes screen time older than <see cref="UsageStore.RetentionDays"/> days: rows of days before today minus the
    /// retention period. Today minus exactly the retention period is kept.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    public Task<int> PruneAsync(CancellationToken cancellationToken = default)
    {
        var today = CurrentDayClock().DayOf(_time.GetUtcNow());
        return _store.PruneAsync(DayClock.Format(today.AddDays(-UsageStore.RetentionDays)), cancellationToken);
    }

    /// <summary>
    /// Adds bonus minutes to a kid's current day. Access rules are the caller's job.
    /// </summary>
    /// <param name="caller">Who adds them, recorded by name.</param>
    /// <param name="userId">The kid.</param>
    /// <param name="minutes">The minutes, 1–240.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new status, the errors keyed <c>Minutes</c>, or not a kid.</returns>
    public async Task<UsageResult> AddBonusAsync(CallerContext caller, Guid userId, int? minutes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var profile = await _profiles.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return new UsageResult(UsageOutcome.NotAKid);
        }

        if (minutes is not (>= 1 and <= 240))
        {
            return new UsageResult(UsageOutcome.Invalid, Errors: new Dictionary<string, string[]> { ["Minutes"] = ["Must be between 1 and 240."] });
        }

        var now = _time.GetUtcNow();
        var day = DayClock.Format(CurrentDayClock().DayOf(now));
        await _store.AddBonusAsync(userId, day, minutes.Value, caller.LogName, now, cancellationToken).ConfigureAwait(false);
        var status = await StatusAsync(userId, profile.Settings.DailyAllowanceMinutes, cancellationToken).ConfigureAwait(false);
        return new UsageResult(UsageOutcome.Saved, status);
    }

    /// <summary>
    /// Adds offline segments a kid reported, each to the day it started in. Access rules are the caller's job.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="segments">The segments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Today's status, the errors keyed <c>Segments[i].Field</c>, or not a kid.</returns>
    public async Task<UsageResult> ReportAsync(Guid userId, IReadOnlyList<SegmentInput>? segments, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return new UsageResult(UsageOutcome.NotAKid);
        }

        var now = _time.GetUtcNow();
        var errors = SegmentValidator.Validate(segments, now);
        if (errors.Count > 0)
        {
            return new UsageResult(UsageOutcome.Invalid, Errors: errors);
        }

        var clock = CurrentDayClock();
        var rows = segments!
            .Select(s => (s.Id!, DayClock.Format(clock.DayOf(s.StartedAt!.Value)), s.StartedAt.Value, s.Seconds!.Value))
            .ToList();
        await _store.AddSegmentsAsync(userId, rows, now, cancellationToken).ConfigureAwait(false);
        var status = await StatusAsync(userId, profile.Settings.DailyAllowanceMinutes, cancellationToken).ConfigureAwait(false);
        return new UsageResult(UsageOutcome.Saved, status);
    }

    /// <inheritdoc />
    public void Dispose() => _flushLock.Dispose();

    private async Task<KidStatus> StatusAsync(Guid userId, int? allowanceMinutes, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var clock = CurrentDayClock();
        var day = clock.DayOf(now);
        var key = DayClock.Format(day);
        var bonus = await _store.GetBonusMinutesAsync(userId, key, cancellationToken).ConfigureAwait(false);

        // Stored and pending are read with no flush in between, which would move time from one to the other unseen
        long stored, pending;
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            stored = await _store.GetUsedMsAsync(userId, key, cancellationToken).ConfigureAwait(false);
            lock (_pendingLock)
            {
                pending = _pending.GetValueOrDefault((userId, key));
            }
        }
        finally
        {
            _flushLock.Release();
        }

        return StatusCalculator.Calculate(day, stored + pending, allowanceMinutes, bonus, now, clock.NextStart(now));
    }
}
