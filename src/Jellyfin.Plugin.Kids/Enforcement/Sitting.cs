using System;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// A kid's current sitting, across all their devices: the playing time since they last had nothing playing for the
/// break length (data-model.md "Sitting", research R7, clarifications Q1 and Q2).
/// </summary>
/// <remarks>Not thread-safe: <see cref="EnforcementState"/> serialises calls. Kept in memory only.</remarks>
public sealed class Sitting
{
    private bool _played;

    /// <summary>Gets the playing time in the current sitting, as last counted.</summary>
    public long UsedMs { get; private set; }

    /// <summary>Gets when the current sitting started; identifies it for warnings.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Gets the end of the last counted interval.</summary>
    public DateTimeOffset LastPlayedAt { get; private set; }

    /// <summary>
    /// Adds a counted interval. A gap of at least the break since the last one starts a new sitting.
    /// </summary>
    /// <param name="from">The interval start.</param>
    /// <param name="to">The interval end.</param>
    /// <param name="breakLength">The household break.</param>
    public void Add(DateTimeOffset from, DateTimeOffset to, TimeSpan breakLength)
    {
        if (to <= from)
        {
            return;
        }

        if (!_played || from - LastPlayedAt >= breakLength)
        {
            UsedMs = 0;
            StartedAt = from;
        }

        UsedMs += (long)(to - from).TotalMilliseconds;
        if (!_played || to > LastPlayedAt)
        {
            LastPlayedAt = to;
        }

        _played = true;
    }

    /// <summary>
    /// Forgets the sitting, for a parent's reset (feature 7): no playing time and no break; the next counted interval
    /// starts a new sitting.
    /// </summary>
    public void Reset()
    {
        _played = false;
        UsedMs = 0;
        StartedAt = default;
        LastPlayedAt = default;
    }

    /// <summary>
    /// Reads the sitting at a moment: after a break with nothing played, it is a new, empty sitting.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="limit">The kid's sitting limit, or null.</param>
    /// <param name="breakLength">The household break.</param>
    /// <returns>The sitting's state.</returns>
    public SittingState Read(DateTimeOffset now, TimeSpan? limit, TimeSpan breakLength)
    {
        var over = !_played || now - LastPlayedAt >= breakLength;
        var used = over ? 0 : UsedMs;
        var reached = limit is { } l && used >= (long)l.TotalMilliseconds;
        return new SittingState(
            used,
            limit is { } lim ? Math.Max(0, (long)lim.TotalMilliseconds - used) : null,
            reached ? LastPlayedAt + breakLength : null,
            over ? null : StartedAt);
    }
}
