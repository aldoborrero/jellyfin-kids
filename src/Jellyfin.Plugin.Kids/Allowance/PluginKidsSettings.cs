using System;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// Reads the household settings from the plugin configuration on every access, in server local time.
/// </summary>
public sealed class PluginKidsSettings : IKidsSettings
{
    private readonly Func<int?> _dayStartsAtHour;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginKidsSettings"/> class over the running plugin.
    /// </summary>
    public PluginKidsSettings()
        : this(() => Plugin.Instance?.Configuration.DayStartsAtHour)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginKidsSettings"/> class over a given setting, so tests need no
    /// Jellyfin types.
    /// </summary>
    /// <param name="dayStartsAtHour">Reads the stored "day starts at" hour; null when there is no configuration.</param>
    internal PluginKidsSettings(Func<int?> dayStartsAtHour)
    {
        _dayStartsAtHour = dayStartsAtHour;
    }

    /// <inheritdoc />
    public int DayStartsAtHour => _dayStartsAtHour() is int hour and >= 0 and <= 23 ? hour : 0;

    /// <inheritdoc />
    public TimeZoneInfo Zone => TimeZoneInfo.Local;
}
