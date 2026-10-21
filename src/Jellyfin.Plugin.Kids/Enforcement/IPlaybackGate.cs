using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// What <see cref="KidsPlaybackFilter"/> asks before letting a playback request through. Every member answers from
/// memory, with no I/O (FR-009).
/// </summary>
public interface IPlaybackGate : IKidLookup
{
    /// <summary>
    /// Gets a value indicating whether the household turned enforcement on.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets the kid's denial, if they may not play now.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="now">The current time.</param>
    /// <param name="denial">The denial.</param>
    /// <returns><c>true</c> if the kid is denied.</returns>
    bool TryGetDenial(Guid kid, DateTimeOffset now, [MaybeNullWhen(false)] out Denial denial);
}
