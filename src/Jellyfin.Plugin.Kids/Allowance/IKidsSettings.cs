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
}
