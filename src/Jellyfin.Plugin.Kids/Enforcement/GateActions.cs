using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// The Jellyfin 12.2 actions that start or continue media playback, which the gate must guard (research R3). The
/// startup self-check reports any that the server does not have.
/// </summary>
public static class GateActions
{
    /// <summary>
    /// Gets the expected actions, by controller name (without the <c>Controller</c> suffix) and action name.
    /// </summary>
    public static IReadOnlyList<(string Controller, string Action, GateKind Kind)> Expected { get; } =
    [
        ("MediaInfo", "GetPlaybackInfo", GateKind.Negotiate),
        ("MediaInfo", "GetPostedPlaybackInfo", GateKind.Negotiate),
        ("MediaInfo", "OpenLiveStream", GateKind.Open),
        ("UniversalAudio", "GetUniversalAudioStream", GateKind.Deliver),
        ("DynamicHls", "GetLiveHlsStream", GateKind.Deliver),
        ("DynamicHls", "GetMasterHlsVideoPlaylist", GateKind.Deliver),
        ("DynamicHls", "GetMasterHlsAudioPlaylist", GateKind.Deliver),
        ("DynamicHls", "GetVariantHlsVideoPlaylist", GateKind.Deliver),
        ("DynamicHls", "GetVariantHlsAudioPlaylist", GateKind.Deliver),
        ("DynamicHls", "GetHlsVideoSegment", GateKind.Deliver),
        ("DynamicHls", "GetHlsAudioSegment", GateKind.Deliver),
        ("Videos", "GetVideoStream", GateKind.Deliver),
        ("Videos", "GetVideoStreamByContainer", GateKind.Deliver),
        ("Audio", "GetAudioStream", GateKind.Deliver),
        ("Audio", "GetAudioStreamByContainer", GateKind.Deliver),
        ("HlsSegment", "GetHlsPlaylistLegacy", GateKind.Deliver),
        ("HlsSegment", "GetHlsVideoSegmentLegacy", GateKind.Deliver),
        ("HlsSegment", "GetHlsAudioSegmentLegacy", GateKind.Deliver),
        ("Library", "GetFile", GateKind.Deliver),
        ("Library", "GetDownload", GateKind.Deliver),
        ("LiveTv", "GetLiveRecordingFile", GateKind.Deliver),
        ("LiveTv", "GetLiveStreamFile", GateKind.Deliver),
    ];

    /// <summary>
    /// Gets the controllers whose media-typed actions are not playback (subtitles, trickplay, attachments), so the
    /// attribute rule skips them.
    /// </summary>
    public static IReadOnlySet<string> ExcludedControllers { get; } = new HashSet<string> { "Subtitle", "Trickplay", "VideoAttachments", "Playstate" };
}
