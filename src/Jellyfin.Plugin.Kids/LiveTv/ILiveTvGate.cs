using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.Enforcement;

namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// The kids' live TV channels, answered from memory for <see cref="KidsLiveTvFilter"/>.
/// </summary>
public interface ILiveTvGate : IKidLookup
{
    /// <summary>
    /// Gets the channels a kid may watch now: the profile's list, or none when the kid's Jellyfin live TV access is
    /// off or the kid is not loaded yet (data-model "Effective channels").
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <returns>The channel ids.</returns>
    IReadOnlySet<Guid> AllowedChannels(Guid kid);
}
