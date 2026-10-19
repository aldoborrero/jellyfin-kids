using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Kids.Configuration;

/// <summary>
/// The Kids plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the hour, in server local time, at which each kid's screen time starts again from zero: 0–23;
    /// out-of-range values read as 0.
    /// </summary>
    public int DayStartsAtHour { get; set; }
}
