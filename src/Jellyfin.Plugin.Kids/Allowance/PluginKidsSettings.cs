using System;
using Jellyfin.Plugin.Kids.Configuration;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// Reads the household settings from the plugin configuration on every access, in server local time.
/// </summary>
public sealed class PluginKidsSettings : IKidsSettings
{
    private readonly Func<PluginConfiguration?> _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginKidsSettings"/> class over the running plugin.
    /// </summary>
    public PluginKidsSettings()
        : this(() => Plugin.Instance?.Configuration)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginKidsSettings"/> class over a given configuration, so tests need
    /// no running plugin.
    /// </summary>
    /// <param name="configuration">Reads the stored configuration; null when there is none.</param>
    internal PluginKidsSettings(Func<PluginConfiguration?> configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public int DayStartsAtHour => _configuration()?.DayStartsAtHour is int hour and >= 0 and <= 23 ? hour : 0;

    /// <inheritdoc />
    public TimeZoneInfo Zone => TimeZoneInfo.Local;

    /// <inheritdoc />
    public bool EnforcementEnabled => _configuration()?.EnforcementEnabled ?? false;

    /// <inheritdoc />
    public TimeSpan SittingBreak => TimeSpan.FromMinutes(
        _configuration()?.SittingBreakMinutes is int minutes and >= 1 and <= 240 ? minutes : PluginConfiguration.DefaultSittingBreakMinutes);
}
