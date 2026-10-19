using System;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// The household settings screen time depends on, read on every use so a dashboard change applies to the next read.
/// </summary>
public interface IKidsSettings
{
    /// <summary>
    /// Gets the hour (0–23) at which a new day starts.
    /// </summary>
    int DayStartsAtHour { get; }

    /// <summary>
    /// Gets the time zone days are counted in.
    /// </summary>
    TimeZoneInfo Zone { get; }

    /// <summary>
    /// Gets a value indicating whether the server enforces kids' limits on every client.
    /// </summary>
    bool EnforcementEnabled { get; }

    /// <summary>
    /// Gets how long a kid must have nothing playing for a new sitting to start (1–240 minutes).
    /// </summary>
    TimeSpan SittingBreak { get; }
}
