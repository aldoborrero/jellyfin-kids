using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// <see cref="IPlaybackControl"/> over Jellyfin's session, transcode and media source managers.
/// </summary>
public sealed class JellyfinPlaybackControl : IPlaybackControl
{
    private readonly ISessionManager _sessions;
    private readonly ITranscodeManager _transcodes;
    private readonly IMediaSourceManager _mediaSources;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPlaybackControl"/> class.
    /// </summary>
    /// <param name="sessions">The session manager.</param>
    /// <param name="transcodes">The transcode manager.</param>
    /// <param name="mediaSources">The media source manager.</param>
    public JellyfinPlaybackControl(ISessionManager sessions, ITranscodeManager transcodes, IMediaSourceManager mediaSources)
    {
        _sessions = sessions;
        _transcodes = transcodes;
        _mediaSources = mediaSources;
    }

    /// <inheritdoc />
    public IReadOnlyList<KidSession> Sessions() => _sessions.Sessions.Select(KidSession.From).ToList();

    /// <inheritdoc />
    public Task SendMessageAsync(string sessionId, string header, string text, long? timeoutMs, CancellationToken cancellationToken)
        // No controlling session: the server sends it as itself, without the "can control" check
        => _sessions.SendMessageCommand(null!, sessionId, new MessageCommand { Header = header, Text = text, TimeoutMs = timeoutMs }, cancellationToken);

    /// <inheritdoc />
    public Task SendStopAsync(string sessionId, CancellationToken cancellationToken)
        => _sessions.SendPlaystateCommand(null!, sessionId, new PlaystateRequest { Command = PlaystateCommand.Stop }, cancellationToken);

    /// <inheritdoc />
    public Task KillTranscodesAsync(string deviceId) => _transcodes.KillTranscodingJobs(deviceId, null, _ => true);

    /// <inheritdoc />
    public Task CloseLiveStreamAsync(string liveStreamId) => _mediaSources.CloseLiveStream(liveStreamId);

    /// <inheritdoc />
    public async Task<bool> EndPlaybackAsync(string sessionId)
    {
        var session = _sessions.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session?.NowPlayingItem is not { } item)
        {
            return false;
        }

        // What SessionManager.CheckForIdlePlayback does for a session that stopped checking in
        await _sessions.OnPlaybackStopped(new PlaybackStopInfo
        {
            Item = item,
            ItemId = item.Id,
            SessionId = session.Id,
            MediaSourceId = session.PlayState?.MediaSourceId,
            PositionTicks = session.LastPlaybackCheckInPositionTicks,
        }).ConfigureAwait(false);
        return true;
    }
}
