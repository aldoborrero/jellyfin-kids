namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// What a guarded playback action does, which decides how a denied kid is answered (research R3, R5).
/// </summary>
public enum GateKind
{
    /// <summary>Negotiates playback (PlaybackInfo): answered 200 with <c>ErrorCode = NotAllowed</c>.</summary>
    Negotiate,

    /// <summary>Opens a live stream: answered 403.</summary>
    Open,

    /// <summary>Delivers media bytes or playlists: answered 403, and tracked while it streams so it can be cut.</summary>
    Deliver
}
