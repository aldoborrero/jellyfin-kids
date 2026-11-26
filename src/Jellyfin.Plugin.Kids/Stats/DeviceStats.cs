namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// Where a kid watches (feature 22): one device and app.
/// </summary>
/// <param name="Device">The device name.</param>
/// <param name="Client">The app name.</param>
/// <param name="Seconds">The seconds over the range.</param>
/// <param name="Plays">How many playbacks.</param>
public sealed record DeviceStats(string Device, string? Client, int Seconds, int Plays);
