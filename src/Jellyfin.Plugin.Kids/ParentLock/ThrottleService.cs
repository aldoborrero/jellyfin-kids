using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// Limits wrong parent codes, per user and device and per user across all devices. State is kept in memory only.
/// </summary>
/// <remarks>
/// After <see cref="DeviceThreshold"/> consecutive wrong codes from one user and device, or
/// <see cref="UserThreshold"/> from one user on any devices, attempts are refused unchecked for 30 seconds. Each wrong
/// code after a wait doubles it, up to 15 minutes. A right code clears both counts. Refused attempts are not counted.
/// </remarks>
public sealed class ThrottleService
{
    /// <summary>
    /// Consecutive wrong codes from one user and device before attempts are refused.
    /// </summary>
    public const int DeviceThreshold = 5;

    /// <summary>
    /// Consecutive wrong codes from one user, on any devices, before attempts are refused.
    /// </summary>
    public const int UserThreshold = 20;

    private static readonly TimeSpan _firstWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _maxWait = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan _keepIdle = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _pruneEvery = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Record> _records = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly ILogger<ThrottleService> _logger;
    private long _lastPruneTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottleService"/> class.
    /// </summary>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public ThrottleService(TimeProvider time, ILogger<ThrottleService> logger)
    {
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Gets how many records are held.
    /// </summary>
    internal int Count => _records.Count;

    /// <summary>
    /// Checks whether a user and device may try a code now.
    /// </summary>
    /// <param name="userKey">The user (or API key).</param>
    /// <param name="deviceKey">The device.</param>
    /// <param name="retryAfterSeconds">When refused, the seconds to wait, rounded up.</param>
    /// <returns><c>true</c> if the code may be checked.</returns>
    public bool TryBegin(string userKey, string deviceKey, out int retryAfterSeconds)
    {
        var now = Now();
        PruneIfDue(now);
        var wait = TimeSpan.Zero;
        foreach (var key in Keys(userKey, deviceKey))
        {
            if (_records.TryGetValue(key, out var record))
            {
                var remaining = record.Touch(now);
                if (remaining > wait)
                {
                    wait = remaining;
                }
            }
        }

        retryAfterSeconds = (int)Math.Ceiling(wait.TotalSeconds);
        return wait == TimeSpan.Zero;
    }

    /// <summary>
    /// Counts a wrong code, and starts or lengthens a wait when a threshold is reached.
    /// </summary>
    /// <param name="userKey">The user (or API key).</param>
    /// <param name="deviceKey">The device.</param>
    /// <param name="who">Who to name in the log: a user name or "API key", never a key or a hash.</param>
    public void RecordFailure(string userKey, string deviceKey, string who)
    {
        var now = Now();
        var keys = Keys(userKey, deviceKey);
        int[] thresholds = [DeviceThreshold, UserThreshold];
        for (var i = 0; i < keys.Length; i++)
        {
            var record = _records.GetOrAdd(keys[i], _ => new Record());
            var (failures, blockedFor) = record.Fail(now, thresholds[i]);
            if (blockedFor is { } wait)
            {
                _logger.LogWarning(
                    "Parent lock attempts blocked for {Who} ({Scope}) for {Seconds} s after {Failures} wrong codes",
                    who,
                    i == 0 ? "this device" : "all devices",
                    (int)wait.TotalSeconds,
                    failures);
            }
        }
    }

    /// <summary>
    /// Clears the counts after a right code.
    /// </summary>
    /// <param name="userKey">The user (or API key).</param>
    /// <param name="deviceKey">The device.</param>
    public void RecordSuccess(string userKey, string deviceKey)
    {
        foreach (var key in Keys(userKey, deviceKey))
        {
            _records.TryRemove(key, out _);
        }
    }

    private static string[] Keys(string userKey, string deviceKey) => [$"d:{userKey}|{deviceKey}", $"u:{userKey}"];

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private void PruneIfDue(DateTime now)
    {
        var last = Interlocked.Read(ref _lastPruneTicks);
        if (now.Ticks - last < _pruneEvery.Ticks || Interlocked.CompareExchange(ref _lastPruneTicks, now.Ticks, last) != last)
        {
            return;
        }

        foreach (var (key, record) in _records)
        {
            if (record.IsIdle(now, _keepIdle))
            {
                _records.TryRemove(key, out _);
            }
        }
    }

    private sealed class Record
    {
        private readonly Lock _gate = new();
        private int _failures;
        private DateTime? _blockedUntil;
        private DateTime _lastSeen;

        // Returns how long the record is still blocked
        public TimeSpan Touch(DateTime now)
        {
            lock (_gate)
            {
                _lastSeen = now;
                return _blockedUntil is { } until && until > now ? until - now : TimeSpan.Zero;
            }
        }

        // Counts a failure; returns the new count and the wait it started, if any
        public (int Failures, TimeSpan? BlockedFor) Fail(DateTime now, int threshold)
        {
            lock (_gate)
            {
                _lastSeen = now;
                _failures++;
                if (_failures < threshold)
                {
                    return (_failures, null);
                }

                var doublings = Math.Min(_failures - threshold, 10);
                var wait = TimeSpan.FromTicks(Math.Min(_firstWait.Ticks << doublings, _maxWait.Ticks));
                _blockedUntil = now + wait;
                return (_failures, wait);
            }
        }

        public bool IsIdle(DateTime now, TimeSpan keepIdle)
        {
            lock (_gate)
            {
                var activeUntil = _blockedUntil is { } until && until > _lastSeen ? until : _lastSeen;
                return now - activeUntil > keepIdle;
            }
        }
    }
}
