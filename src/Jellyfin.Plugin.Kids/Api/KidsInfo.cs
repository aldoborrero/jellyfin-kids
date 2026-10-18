using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// What clients need to know about the installed plugin. Nothing household-specific: the lock type is read after
/// signing in, from <c>GET /Kids/Parent/Lock</c>.
/// </summary>
/// <param name="PluginVersion">The plugin version.</param>
/// <param name="ApiVersion">The version of the client API, raised on breaking changes.</param>
/// <param name="Features">The capabilities this plugin offers; later features only add to the list.</param>
public record KidsInfo(string PluginVersion, int ApiVersion, IReadOnlyList<string> Features);
