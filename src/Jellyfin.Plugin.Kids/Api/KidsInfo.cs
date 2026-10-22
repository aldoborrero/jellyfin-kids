using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// What clients need to know about the installed plugin. Nothing household-specific: the lock type is read after
/// signing in, from <c>GET /Kids/Parent/Lock</c>, and whether enforcement is on from the kid's Status.
/// </summary>
/// <param name="PluginVersion">The plugin version.</param>
/// <param name="ApiVersion">The version of the client API, raised on breaking changes.</param>
/// <param name="Features">The capabilities this plugin offers; later features only add to the list.</param>
/// <param name="Enforcement">Whether the plugin could guard every playback route on this server.</param>
/// <param name="LiveTv">Whether the plugin could attach its live TV rules to every live TV route on this server.</param>
public record KidsInfo(string PluginVersion, int ApiVersion, IReadOnlyList<string> Features, EnforcementInfo? Enforcement = null, EnforcementInfo? LiveTv = null)
{
    /// <summary>
    /// Gets the capabilities of this plugin version, in the order they were added. Null fields are omitted from
    /// answers, so clients rely on these names (for example <c>sessionLimit</c>) to know a field is supported.
    /// </summary>
    public static IReadOnlyList<string> SupportedFeatures { get; } = ["profiles", "parentLock", "allowance", "sessionLimit", "enforcement", "sitting", "liveTv", "watchTimeReset"];
}
