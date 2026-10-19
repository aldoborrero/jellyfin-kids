using System;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// What enforcement needs to know about a Jellyfin session, so its logic is tested without Jellyfin's types.
/// </summary>
/// <param name="Id">The session id.</param>
/// <param name="UserId">The owner.</param>
/// <param name="UserName">The owner's name, for logs.</param>
/// <param name="Client">The client app, for logs.</param>
/// <param name="DeviceId">The device id.</param>
/// <param name="IsPlaying">Whether the server shows something playing (paused or not).</param>
/// <param name="IsPaused">Whether it is paused.</param>
/// <param name="LiveStreamId">The live stream the playback opened, if any.</param>
public sealed record KidSession(
    string Id,
    Guid UserId,
    string? UserName,
    string? Client,
    string? DeviceId,
    bool IsPlaying,
    bool IsPaused,
    string? LiveStreamId)
{
    /// <summary>
    /// Reads a Jellyfin session.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>The view.</returns>
    public static KidSession From(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new KidSession(
            session.Id,
            session.UserId,
            session.UserName,
            session.Client,
            session.DeviceId,
            session.NowPlayingItem is not null,
            session.PlayState?.IsPaused ?? false,
            session.PlayState?.LiveStreamId);
    }
}
