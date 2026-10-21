namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// What <see cref="KidsLiveTvFilter"/> does on an action for a kid (data-model "Request judgement").
/// </summary>
public enum LiveTvRule
{
    /// <summary>A playback action: refused when it names a channel outside the kid's set, or an unknown live TV stream.</summary>
    Playback,

    /// <summary>A recording, timer or series timer action, or a live recording stream: always refused.</summary>
    Recording,

    /// <summary><c>GET LiveTv/Channels</c>: cut to the kid's channels, then paged.</summary>
    ChannelList,

    /// <summary><c>GET LiveTv/Channels/{id}</c>: 404 for a channel outside the kid's set.</summary>
    Channel,

    /// <summary><c>GET LiveTv/Programs</c>: its <c>channelIds</c> narrowed to the kid's channels.</summary>
    ProgramList,

    /// <summary><c>POST LiveTv/Programs</c>: its body's <c>ChannelIds</c> narrowed to the kid's channels.</summary>
    ProgramListPosted,

    /// <summary><c>GET LiveTv/Programs/Recommended</c>: cut to the kid's channels, then paged.</summary>
    RecommendedPrograms,

    /// <summary><c>GET LiveTv/Programs/{id}</c>: 404 for a program of a channel outside the kid's set.</summary>
    Program
}
