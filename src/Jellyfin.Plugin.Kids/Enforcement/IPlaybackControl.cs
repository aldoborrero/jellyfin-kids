using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// The Jellyfin actions enforcement takes on a session (research R9), behind one seam so they are tested with a fake.
/// </summary>
public interface IPlaybackControl
{
    /// <summary>
    /// Gets the current sessions.
    /// </summary>
    /// <returns>The sessions.</returns>
    IReadOnlyList<KidSession> Sessions();

    /// <summary>
    /// Shows a message on a session: a toast with a timeout, or a dialog without one.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="header">The title.</param>
    /// <param name="text">The text.</param>
    /// <param name="timeoutMs">How long a toast shows, or null for a dialog.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when it was sent.</returns>
    Task SendMessageAsync(string sessionId, string header, string text, long? timeoutMs, CancellationToken cancellationToken);

    /// <summary>
    /// Tells a session's client to stop playing.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when it was sent.</returns>
    Task SendStopAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Ends every transcode of a device and deletes its files.
    /// </summary>
    /// <param name="deviceId">The device.</param>
    /// <returns>A task that completes when they are ended.</returns>
    Task KillTranscodesAsync(string deviceId);

    /// <summary>
    /// Closes a live stream.
    /// </summary>
    /// <param name="liveStreamId">The live stream.</param>
    /// <returns>A task that completes when it is closed.</returns>
    Task CloseLiveStreamAsync(string liveStreamId);

    /// <summary>
    /// Ends a session's playback on the server, as Jellyfin's idle check does, so the server stops treating it as
    /// playing.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <returns><c>true</c> if it was playing.</returns>
    Task<bool> EndPlaybackAsync(string sessionId);
}
