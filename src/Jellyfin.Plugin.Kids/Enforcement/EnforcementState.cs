using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Jellyfin.Data;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.LiveTv;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Kids' limits in memory: today's budget, the sitting and a copy of the Jellyfin user for the allowed hours, so the
/// playback tracker and the request filter decide without I/O (research R6–R8). This is the deny set: a kid is denied
/// while <see cref="Evaluate"/> returns a denial.
/// </summary>
/// <remarks>
/// <see cref="EnforcementService"/> loads each kid at startup and again when something changes; the tracker adds
/// counted time as it happens. A kid not loaded yet is never denied for time (fail open) and asks for a load.
/// </remarks>
public sealed class EnforcementState : IPlaybackGate, ILiveTvGate
{
    /// <summary>How often a kid that cannot be loaded asks again.</summary>
    public static readonly TimeSpan ReloadRetry = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan WarnHorizon = TimeSpan.FromSeconds(StatusCalculator.WarnAtSeconds.Max());

    private static readonly IReadOnlySet<Guid> Empty = new HashSet<Guid>();

    private readonly KidRoster _roster;
    private readonly IKidsSettings _settings;
    private readonly IPlaybackControl _control;
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, KidState> _kids = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="EnforcementState"/> class.
    /// </summary>
    /// <param name="roster">The kid ids.</param>
    /// <param name="settings">The household settings.</param>
    /// <param name="control">The sessions, to find a kid by device.</param>
    public EnforcementState(KidRoster roster, IKidsSettings settings, IPlaybackControl control)
    {
        _roster = roster;
        _settings = settings;
        _control = control;
    }

    /// <summary>
    /// Raised, outside any lock, when a kid needs loading: never loaded, or a new day started.
    /// </summary>
    public event EventHandler<GenericEventArgs<Guid>>? ReloadRequested;

    /// <inheritdoc />
    public bool IsEnabled => _settings.EnforcementEnabled;

    /// <inheritdoc />
    public bool IsKid(Guid userId) => _roster.Contains(userId);

    /// <summary>
    /// Replaces what is known about a kid, keeping their sitting.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="budget">Today's budget.</param>
    /// <param name="sittingLimitMinutes">The sitting limit, or null.</param>
    /// <param name="user">The Jellyfin user, for the allowed hours and live TV access; null if not found.</param>
    /// <param name="liveTvChannels">The live TV channels the profile allows; none if null.</param>
    public void SetKid(Guid kid, KidBudget budget, int? sittingLimitMinutes, User? user, IReadOnlyCollection<Guid>? liveTvChannels = null)
    {
        lock (_lock)
        {
            var state = GetOrAdd(kid);
            state.Budget = budget;
            state.SittingLimitMinutes = sittingLimitMinutes;
            state.LiveTvChannels = liveTvChannels is null ? Empty : new HashSet<Guid>(liveTvChannels);
            state.User = user;
            state.ReloadAskedAt = null;
        }
    }

    /// <summary>
    /// Replaces a kid's Jellyfin user, after the server changed it.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="user">The user.</param>
    public void SetUser(Guid kid, User user)
    {
        lock (_lock)
        {
            if (_kids.TryGetValue(kid, out var state))
            {
                state.User = user;
            }
        }
    }

    /// <summary>
    /// Forgets a kid who is no longer one.
    /// </summary>
    /// <param name="kid">The user.</param>
    public void Forget(Guid kid)
    {
        lock (_lock)
        {
            _kids.Remove(kid);
        }
    }

    /// <summary>
    /// Gets the kids that are loaded.
    /// </summary>
    /// <returns>Their ids.</returns>
    public IReadOnlyList<Guid> LoadedKids()
    {
        lock (_lock)
        {
            return _kids.Where(k => k.Value.Budget is not null).Select(k => k.Key).ToList();
        }
    }

    /// <summary>
    /// Adds counted playing time, from the tracker. No I/O.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="from">The interval start.</param>
    /// <param name="to">The interval end.</param>
    public void OnPlayed(Guid kid, DateTimeOffset from, DateTimeOffset to)
    {
        bool reload;
        lock (_lock)
        {
            var state = GetOrAdd(kid);
            state.Sitting.Add(from, to, _settings.SittingBreak);
            reload = false;
            if (state.Budget is { } budget)
            {
                foreach (var (day, length) in Clock().Split(from, to))
                {
                    reload |= budget.Add(day, (long)length.TotalMilliseconds);
                }
            }
            else
            {
                reload = AskReload(state, to);
            }
        }

        if (reload)
        {
            ReloadRequested?.Invoke(this, new GenericEventArgs<Guid>(kid));
        }
    }

    /// <summary>
    /// Decides whether a kid may play now, whether or not enforcement is on.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The denial, or null if the kid may play (or is not a kid).</returns>
    public Denial? Evaluate(Guid kid, DateTimeOffset now)
    {
        if (!_roster.Contains(kid))
        {
            return null;
        }

        Denial? denial;
        bool reload;
        lock (_lock)
        {
            denial = EvaluateLocked(kid, now, out reload);
        }

        if (reload)
        {
            ReloadRequested?.Invoke(this, new GenericEventArgs<Guid>(kid));
        }

        return denial;
    }

    /// <inheritdoc />
    public bool TryGetDenial(Guid kid, DateTimeOffset now, [MaybeNullWhen(false)] out Denial denial)
    {
        denial = null;
        if (!IsEnabled || Evaluate(kid, now) is not { } found)
        {
            return false;
        }

        denial = found;
        return true;
    }

    /// <inheritdoc />
    public IReadOnlySet<Guid> AllowedChannels(Guid kid)
    {
        if (!_roster.Contains(kid))
        {
            return Empty;
        }

        IReadOnlySet<Guid> allowed;
        var reload = false;
        lock (_lock)
        {
            var state = GetOrAdd(kid);
            if (state.Budget is null)
            {
                // Not loaded yet: no live TV until it is (a content rule fails closed for the kid)
                reload = AskReload(state, DateTimeOffset.UtcNow);
                allowed = Empty;
            }
            else
            {
                // Jellyfin's all-or-nothing switch wins over the list
                allowed = state.User?.HasPermission(PermissionKind.EnableLiveTvAccess) == true ? state.LiveTvChannels : Empty;
            }
        }

        if (reload)
        {
            ReloadRequested?.Invoke(this, new GenericEventArgs<Guid>(kid));
        }

        return allowed;
    }

    /// <inheritdoc />
    public Guid KidForDevice(string deviceId)
        => _control.Sessions().FirstOrDefault(s => s.DeviceId == deviceId && _roster.Contains(s.UserId))?.UserId ?? Guid.Empty;

    /// <summary>
    /// The limits that end within the warning horizon (5 minutes), with how long is left.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The limits ending soon.</returns>
    public IReadOnlyList<LimitEnding> EndingSoon(Guid kid, DateTimeOffset now)
    {
        var horizon = (long)WarnHorizon.TotalMilliseconds;
        var ending = new List<LimitEnding>();
        lock (_lock)
        {
            if (!_kids.TryGetValue(kid, out var state))
            {
                return ending;
            }

            if (state.Budget is { } budget && budget.Day == Clock().DayOf(now) && budget.RemainingMs is > 0 and var left && left <= horizon)
            {
                ending.Add(new LimitEnding(DenialReason.TimeUp, DayClock.Format(budget.Day), left));
            }

            if (Sitting(state, now) is { RemainingMs: > 0 and var sittingLeft, StartedAt: { } started } && sittingLeft <= horizon)
            {
                ending.Add(new LimitEnding(DenialReason.SessionLimit, started.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), sittingLeft));
            }

            // Most kids have no schedule: skip the copy and the search
            if (state.User is { AccessSchedules.Count: > 0 } user
                && ScheduleClock.ClosesWithin([.. user.AccessSchedules], now, _settings.Zone, WarnHorizon) is { } closes)
            {
                var key = closes.UtcDateTime.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
                ending.Add(new LimitEnding(DenialReason.OutsideSchedule, key, (long)(closes - now).TotalMilliseconds));
            }
        }

        return ending;
    }

    /// <summary>
    /// Describes a kid's enforcement state for their Status.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The description.</returns>
    public EnforcementView Describe(Guid kid, DateTimeOffset now)
    {
        var enforced = IsEnabled;
        var denial = enforced ? Evaluate(kid, now) : null;
        lock (_lock)
        {
            if (!_kids.TryGetValue(kid, out var state) || state.SittingLimitMinutes is null)
            {
                return new EnforcementView(enforced, denial, null, null, _settings.SittingBreak);
            }

            return new EnforcementView(enforced, denial, Sitting(state, now), TimeSpan.FromMinutes(state.SittingLimitMinutes.Value), _settings.SittingBreak);
        }
    }

    private Denial? EvaluateLocked(Guid kid, DateTimeOffset now, out bool reload)
    {
        reload = false;
        var state = GetOrAdd(kid);
        if (state.Budget is null)
        {
            reload = AskReload(state, now);
        }

        // Jellyfin's own rule, on the cached user (research R8)
        if (state.User is { } user && !user.IsParentalScheduleAllowed())
        {
            return new Denial(DenialReason.OutsideSchedule, null);
        }

        if (state.Budget is { } budget)
        {
            var clock = Clock();
            reload |= budget.RollTo(clock.DayOf(now));
            if (budget.TimeUp)
            {
                return new Denial(DenialReason.TimeUp, clock.NextStart(now));
            }
        }

        return Sitting(state, now) is { BreakEndsAt: { } breakEnds }
            ? new Denial(DenialReason.SessionLimit, breakEnds)
            : null;
    }

    private SittingState? Sitting(KidState state, DateTimeOffset now)
        => state.SittingLimitMinutes is int minutes
            ? state.Sitting.Read(now, TimeSpan.FromMinutes(minutes), _settings.SittingBreak)
            : null;

    private bool AskReload(KidState state, DateTimeOffset now)
    {
        if (state.ReloadAskedAt is { } asked && now - asked < ReloadRetry)
        {
            return false;
        }

        state.ReloadAskedAt = now;
        return true;
    }

    private DayClock Clock() => new(_settings.Zone, _settings.DayStartsAtHour);

    private KidState GetOrAdd(Guid kid)
    {
        if (!_kids.TryGetValue(kid, out var state))
        {
            state = new KidState();
            _kids[kid] = state;
        }

        return state;
    }

    private sealed class KidState
    {
        public IReadOnlySet<Guid> LiveTvChannels { get; set; } = Empty;

        public KidBudget? Budget { get; set; }

        public int? SittingLimitMinutes { get; set; }

        public User? User { get; set; }

        public Sitting Sitting { get; } = new();

        public DateTimeOffset? ReloadAskedAt { get; set; }
    }
}
