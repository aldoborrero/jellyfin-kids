using System.Collections.Generic;
using Jellyfin.Plugin.Kids.Enforcement;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// Whether the plugin could guard every playback route on this server (research R10). Describes the plugin and the
/// Jellyfin version, not the household.
/// </summary>
/// <param name="Gate"><c>ok</c>, <c>degraded</c> (some routes are missing) or <c>pending</c> (not checked yet).</param>
/// <param name="Missing">The expected routes that were not found, as <c>Controller.Action</c>.</param>
public record EnforcementInfo(string Gate, IReadOnlyList<string> Missing)
{
    /// <summary>
    /// Reads the startup check.
    /// </summary>
    /// <param name="coverage">What the convention guarded.</param>
    /// <returns>The info.</returns>
    public static EnforcementInfo From(GateCoverage coverage)
    {
        System.ArgumentNullException.ThrowIfNull(coverage);
        var state = coverage.State;
        return new EnforcementInfo(Name(state), state == GateCoverage.CheckState.Pending ? [] : coverage.Missing);
    }

    /// <summary>
    /// Reads the startup check of the live TV routes (feature 6).
    /// </summary>
    /// <param name="coverage">What the convention attached to.</param>
    /// <returns>The info.</returns>
    public static EnforcementInfo FromLiveTv(GateCoverage coverage)
    {
        System.ArgumentNullException.ThrowIfNull(coverage);
        var state = coverage.LiveTvState;
        return new EnforcementInfo(Name(state), state == GateCoverage.CheckState.Pending ? [] : coverage.LiveTvMissing);
    }

    private static string Name(GateCoverage.CheckState state) => state switch
    {
        GateCoverage.CheckState.Ok => "ok",
        GateCoverage.CheckState.Degraded => "degraded",
        _ => "pending"
    };
}
