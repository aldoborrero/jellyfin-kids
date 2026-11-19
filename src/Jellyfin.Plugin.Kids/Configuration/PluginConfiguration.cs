using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Kids.Configuration;

/// <summary>
/// The Kids plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>The default sitting break, in minutes.</summary>
    public const int DefaultSittingBreakMinutes = 15;

    /// <summary>The default number of days screen time and the watch log are kept.</summary>
    public const int DefaultStatsRetentionDays = 90;

    private static readonly int[] _retentionChoices = [30, 90, 180, 365];

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

    /// <summary>
    /// Gets or sets a value indicating whether the plugin keeps a watch log of what each kid plays, when and on which
    /// device (feature 22, "advanced statistics"). Off by default. Turning it off stops recording and keeps what was
    /// stored until it ages out or is deleted from the statistics page.
    /// </summary>
    public bool AdvancedStats { get; set; }

    /// <summary>
    /// Gets or sets how many days screen time and the watch log are kept: 30, 90, 180 or 365; other values read as 90.
    /// Older days are deleted by the daily prune task.
    /// </summary>
    public int StatsRetentionDays { get; set; } = DefaultStatsRetentionDays;

    /// <summary>
    /// Whether a number of days is one of the retention periods the plugin offers.
    /// </summary>
    /// <param name="days">The days.</param>
    /// <returns><c>true</c> for 30, 90, 180 or 365.</returns>
    public static bool IsRetentionChoice(int days) => Array.IndexOf(_retentionChoices, days) >= 0;
}
