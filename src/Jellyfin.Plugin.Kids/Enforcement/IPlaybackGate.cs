using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// What <see cref="KidsPlaybackFilter"/> asks before letting a playback request through. Every member answers from
/// memory, with no I/O (FR-009).
/// </summary>
public interface IPlaybackGate
{
    /// <summary>
    /// Gets a value indicating whether the household turned enforcement on.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Whether a user is a kid.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns><c>true</c> for a kid.</returns>
    bool IsKid(Guid userId);

    /// <summary>
    /// Gets the kid's denial, if they may not play now.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="now">The current time.</param>
    /// <param name="denial">The denial.</param>
    /// <returns><c>true</c> if the kid is denied.</returns>
    bool TryGetDenial(Guid kid, DateTimeOffset now, [MaybeNullWhen(false)] out Denial denial);

    /// <summary>
    /// Finds the kid who owns a current session on a device, for requests that carry no user.
    /// </summary>
    /// <param name="deviceId">The device id from the request.</param>
    /// <returns>The kid, or <see cref="Guid.Empty"/>.</returns>
    Guid KidForDevice(string deviceId);
}
