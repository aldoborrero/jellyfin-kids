using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Acts on kids' playing sessions when a limit is near or reached (research R9): warnings, then once per denial the
/// time's-up message, Stop, the transcode kill, the live stream close, the cut of in-flight deliveries and the end of
/// the playback on the server. Only a real playback report from a denied session, or the sweep, repeats the stop part,
/// at most every <see cref="RepeatInterval"/>.
/// </summary>
/// <remarks>
/// Called from the tracker for every kid playback event, including Jellyfin's one-second automatic ones, so the
/// decision is in memory and every action runs off the caller's thread. Nothing here throws to the caller.
/// </remarks>
public sealed class PlaybackEnforcer
{
    /// <summary>The shortest time between two stops of the same session.</summary>
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(10);

    private readonly EnforcementState _state;
    private readonly IPlaybackControl _control;
    private readonly DeliveryRegistry _deliveries;
    private readonly IKidsSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<PlaybackEnforcer> _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, SessionRecord> _records = [];
    private readonly Dictionary<Guid, DenialReason> _loggedDenials = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackEnforcer"/> class.
    /// </summary>
    /// <param name="state">The kids' limits.</param>
    /// <param name="control">The Jellyfin actions.</param>
    /// <param name="deliveries">The in-flight deliveries.</param>
    /// <param name="settings">The household settings.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackEnforcer(EnforcementState state, IPlaybackControl control, DeliveryRegistry deliveries, IKidsSettings settings, TimeProvider time, ILogger<PlaybackEnforcer> logger)
    {
        _state = state;
        _control = control;
        _deliveries = deliveries;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Gets or sets how long the time's-up message shows before Stop, so it can be read.
    /// </summary>
    public TimeSpan MessageGrace { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how the grace is waited, so tests control when it ends.
    /// </summary>
    internal Func<TimeSpan, Task>? Wait { get; set; }

    /// <summary>
    /// Handles a kid's playback start or progress event.
    /// </summary>
    /// <param name="session">The session that reported.</param>
    /// <param name="automated">Whether it is Jellyfin's own one-second progress event.</param>
    /// <returns>The work started, for tests; callers ignore it.</returns>
    public Task OnPlayback(KidSession session, bool automated)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            return Handle(session, automated, _time.GetUtcNow());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kids enforcement failed for a playback event");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Catches denied kids' sessions that are still shown as playing without reporting: called every 30 seconds.
    /// </summary>
    /// <returns>The work started, for tests.</returns>
    public Task Sweep()
    {
        try
        {
            var now = _time.GetUtcNow();
            var work = _control.Sessions()
                .Where(s => s.IsPlaying && !s.IsPaused && _state.IsKid(s.UserId))
                .Select(s => Handle(s, automated: false, now))
                .ToList();
            return Task.WhenAll(work);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kids enforcement sweep failed");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Forgets a session that ended.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    public void Forget(string sessionId)
    {
        lock (_lock)
        {
            _records.Remove(sessionId);
        }
    }

    private Task Handle(KidSession session, bool automated, DateTimeOffset now)
    {
        if (!_settings.EnforcementEnabled)
        {
            Forget(session.Id);
            return Task.CompletedTask;
        }

        var kid = session.UserId;
        var denial = _state.Evaluate(kid, now);
        if (denial is null)
        {
            return Warn(session, now);
        }

        bool full;
        bool logDenial;
        lock (_lock)
        {
            var record = Record(session.Id);
            if (record.Denial != denial.Reason)
            {
                record.Denial = denial.Reason;
                record.LastStopAt = now;
                full = true;
            }
            else if (!automated && now - record.LastStopAt >= RepeatInterval)
            {
                record.LastStopAt = now;
                full = false;
            }
            else
            {
                return Task.CompletedTask;
            }

            logDenial = !_loggedDenials.TryGetValue(kid, out var logged) || logged != denial.Reason;
            _loggedDenials[kid] = denial.Reason;
        }

        if (logDenial)
        {
            _logger.LogInformation("Kids: {User} may not play now ({Reason}); stopping their playback", session.UserName ?? kid.ToString("N"), denial.Code);
        }

        return Task.Run(() => StopSafeAsync(session, denial, full, now));
    }

    private Task Warn(KidSession session, DateTimeOffset now)
    {
        List<(string Header, string Text)> messages = [];
        lock (_lock)
        {
            _loggedDenials.Remove(session.UserId);
            var record = Record(session.Id);
            record.Denial = null;
            foreach (var ending in _state.EndingSoon(session.UserId, now))
            {
                var thresholds = StatusCalculator.WarnAtSeconds.Where(t => ending.RemainingMs <= t * 1000L).ToList();
                if (thresholds.Count == 0)
                {
                    continue;
                }

                // The smallest threshold reached; the larger ones are past, so they are never sent afterwards
                var key = $"{ending.Limit}|{thresholds.Min()}|{ending.Instance}";
                if (record.Warned.Add(key))
                {
                    foreach (var t in thresholds)
                    {
                        record.Warned.Add($"{ending.Limit}|{t}|{ending.Instance}");
                    }

                    messages.Add(EnforcementMessages.Warning(ending.Limit, ending.RemainingMs));
                }
            }
        }

        if (messages.Count == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            foreach (var (header, text) in messages)
            {
                await Step("warning", session, () => _control.SendMessageAsync(session.Id, header, text, EnforcementMessages.WarningTimeoutMs, CancellationToken.None)).ConfigureAwait(false);
            }
        });
    }

    private async Task StopSafeAsync(KidSession session, Denial denial, bool full, DateTimeOffset now)
    {
        try
        {
            await StopAsync(session, denial, full, now).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Nothing awaits this work, so nothing must escape it
            _logger.LogError(ex, "Kids enforcement could not stop session {Session}", session.Id);
        }
    }

    private async Task StopAsync(KidSession session, Denial denial, bool full, DateTimeOffset now)
    {
        if (full)
        {
            var (header, text) = EnforcementMessages.Denied(denial, now);
            await Step("message", session, () => _control.SendMessageAsync(session.Id, header, text, null, CancellationToken.None)).ConfigureAwait(false);
            if (MessageGrace > TimeSpan.Zero)
            {
                await (Wait?.Invoke(MessageGrace) ?? Task.Delay(MessageGrace, _time)).ConfigureAwait(false);

                // A bonus or the switch may have lifted the denial while the message showed: then nothing is stopped,
                // and a later denial starts over
                if (!_settings.EnforcementEnabled || _state.Evaluate(session.UserId, _time.GetUtcNow())?.Reason != denial.Reason)
                {
                    lock (_lock)
                    {
                        if (_records.TryGetValue(session.Id, out var record) && record.Denial == denial.Reason)
                        {
                            record.Denial = null;
                        }
                    }

                    _logger.LogDebug("Kids: {User} may play again; not stopping their playback", session.UserName ?? session.UserId.ToString("N"));
                    return;
                }
            }
        }

        await Step("stop", session, () => _control.SendStopAsync(session.Id, CancellationToken.None)).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(session.DeviceId))
        {
            await Step("transcode kill", session, () => _control.KillTranscodesAsync(session.DeviceId)).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(session.LiveStreamId))
        {
            await Step("live stream close", session, () => _control.CloseLiveStreamAsync(session.LiveStreamId)).ConfigureAwait(false);
        }

        var cut = _deliveries.AbortAll(session.UserId);
        var ended = false;
        await Step("end of playback", session, async () => ended = await _control.EndPlaybackAsync(session.Id).ConfigureAwait(false)).ConfigureAwait(false);
        if (full)
        {
            _logger.LogInformation(
                "Kids: stopped {User}'s playback on {Client} ({Reason}); {Cut} stream(s) cut",
                session.UserName ?? session.UserId.ToString("N"),
                session.Client,
                denial.Code,
                cut);
        }
        else
        {
            _logger.LogDebug(
                "Kids: stopped {User}'s playback on {Client} again ({Reason}); {Cut} stream(s) cut, playback ended: {Ended}",
                session.UserName ?? session.UserId.ToString("N"),
                session.Client,
                denial.Code,
                cut,
                ended);
        }
    }

    private async Task Step(string what, KidSession session, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A client without a socket, a session that just ended: the next steps still run
            _logger.LogDebug(ex, "Kids enforcement: {Step} failed for session {Session}", what, session.Id);
        }
    }

    private SessionRecord Record(string sessionId)
    {
        if (!_records.TryGetValue(sessionId, out var record))
        {
            record = new SessionRecord();
            _records[sessionId] = record;
        }

        return record;
    }

    private sealed class SessionRecord
    {
        public DenialReason? Denial { get; set; }

        public DateTimeOffset LastStopAt { get; set; }

        public HashSet<string> Warned { get; } = [];
    }
}
