using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
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
    /// Raised after a bonus or a report changed a kid's time, so enforcement reloads the kid. Handlers must not throw.
    /// </summary>
    public event EventHandler<GenericEventArgs<Guid>>? UsageChanged;

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
    /// <param name="caller">Who adds them, recorded as <see cref="CallerContext.ActorName"/>.</param>
    /// <param name="userId">The kid.</param>
    /// <param name="minutes">The minutes, 1–240.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new status with the bonus and until when it can be undone, the errors keyed <c>Minutes</c>, or not a kid.</returns>
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
        var clock = CurrentDayClock();
        var day = DayClock.Format(clock.DayOf(now));
        var id = await _store.AddBonusAsync(userId, day, minutes.Value, caller.ActorName, now, cancellationToken).ConfigureAwait(false);
        UsageChanged?.Invoke(this, new GenericEventArgs<Guid>(userId));
        var status = await StatusAsync(userId, profile.Settings.DailyAllowanceMinutes, cancellationToken).ConfigureAwait(false);
        var bonus = new StoredBonus(id, day, minutes.Value, caller.ActorName, now);
        return new UsageResult(UsageOutcome.Saved, status, Bonus: bonus, BonusUndoUntil: BonusUndo.UndoUntil(now, clock.NextStart(now)));
    }

    /// <summary>
    /// Gets a kid's bonuses of the current day (feature 13). Access rules are the caller's job.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Today's bonuses, newest first, or null if the user is not a kid.</returns>
    public async Task<TodayBonuses?> ListBonusesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        var now = _time.GetUtcNow();
        var clock = CurrentDayClock();
        var today = clock.DayOf(now);
        var key = DayClock.Format(today);
        var bonuses = await _store.ListBonusesAsync(userId, key, cancellationToken).ConfigureAwait(false);
        return new TodayBonuses(
            today,
            now,
            bonuses.Select(b =>
            {
                var until = BonusUndo.UndoUntil(b.CreatedAt, clock.NextStart(b.CreatedAt));
                return new BonusState(b, until, BonusUndo.CanUndo(b.Day, key, until, now));
            }).ToList());
    }

    /// <summary>
    /// Undoes one of a kid's bonuses (feature 13): only today's, and only within <see cref="BonusUndo.Window"/> of
    /// when it was given. Access rules are the caller's job.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="bonusId">The bonus id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new status with the undone bonus; not a kid; not found; or too late (with the bonus).</returns>
    public async Task<UsageResult> UndoBonusAsync(Guid userId, long bonusId, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return new UsageResult(UsageOutcome.NotAKid);
        }

        var bonus = await _store.GetBonusAsync(userId, bonusId, cancellationToken).ConfigureAwait(false);
        if (bonus is null)
        {
            return new UsageResult(UsageOutcome.NotFound);
        }

        var now = _time.GetUtcNow();
        var clock = CurrentDayClock();
        var until = BonusUndo.UndoUntil(bonus.CreatedAt, clock.NextStart(bonus.CreatedAt));
        if (!BonusUndo.CanUndo(bonus.Day, DayClock.Format(clock.DayOf(now)), until, now))
        {
            return new UsageResult(UsageOutcome.TooLate, Bonus: bonus);
        }

        // Two undos of the same id: the one that deletes nothing is told it is gone
        if (!await _store.DeleteBonusAsync(userId, bonusId, cancellationToken).ConfigureAwait(false))
        {
            return new UsageResult(UsageOutcome.NotFound);
        }

        UsageChanged?.Invoke(this, new GenericEventArgs<Guid>(userId));
        var status = await StatusAsync(userId, profile.Settings.DailyAllowanceMinutes, cancellationToken).ConfigureAwait(false);
        return new UsageResult(UsageOutcome.Saved, status, Bonus: bonus);
    }

    /// <summary>
    /// Sets a kid's used time and bonus for the current day back to 0 (feature 7). Serialised with flushes, so a flush
    /// in progress cannot write the cleared time back. Access rules are the caller's job.
    /// </summary>
    /// <param name="userId">The kid.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was cleared and the new status, or null if the user is not a kid.</returns>
    public async Task<DayReset?> ResetDayAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        var day = DayClock.Format(CurrentDayClock().DayOf(_time.GetUtcNow()));
        long usedMs;
        int bonusMinutes;
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The store first: if it fails, the pending time is still there and nothing was reset
            (usedMs, bonusMinutes) = await _store.ResetDayAsync(userId, day, cancellationToken).ConfigureAwait(false);
            lock (_pendingLock)
            {
                usedMs += _pending.GetValueOrDefault((userId, day));
                _pending.Remove((userId, day));
            }
        }
        finally
        {
            _flushLock.Release();
        }

        UsageChanged?.Invoke(this, new GenericEventArgs<Guid>(userId));
        var status = await StatusAsync(userId, profile.Settings.DailyAllowanceMinutes, cancellationToken).ConfigureAwait(false);
        return new DayReset(day, usedMs, bonusMinutes, status);
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
        UsageChanged?.Invoke(this, new GenericEventArgs<Guid>(userId));
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
