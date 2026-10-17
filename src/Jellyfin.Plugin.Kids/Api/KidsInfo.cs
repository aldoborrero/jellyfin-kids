namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// What clients need to know about the installed plugin.
/// </summary>
/// <param name="PluginVersion">The plugin version.</param>
/// <param name="ApiVersion">The version of the client API, raised on breaking changes.</param>
public record KidsInfo(string PluginVersion, int ApiVersion);
