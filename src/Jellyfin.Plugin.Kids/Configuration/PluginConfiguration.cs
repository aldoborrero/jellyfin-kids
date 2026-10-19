using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Kids.Configuration;

/// <summary>
/// The Kids plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>The default sitting break, in minutes.</summary>
    public const int DefaultSittingBreakMinutes = 15;

    /// <summary>
    /// Gets or sets the hour, in server local time, at which each kid's screen time starts again from zero: 0–23;
    /// out-of-range values read as 0.
    /// </summary>
    public int DayStartsAtHour { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server enforces kids' limits on every client: warnings, stops and
    /// refused playback. Off by default, so an update changes nothing on its own.
    /// </summary>
    public bool EnforcementEnabled { get; set; }

    /// <summary>
    /// Gets or sets how long, in minutes, a kid must have nothing playing for a new sitting to start, and how long they
    /// wait after reaching their sitting limit: 1–240; other values read as 15.
    /// </summary>
    public int SittingBreakMinutes { get; set; } = DefaultSittingBreakMinutes;
}
