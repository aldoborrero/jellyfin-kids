using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// The Jellyfin 12.2 live TV actions the live TV rules attach to besides the playback actions (research R6, R7). The
/// startup self-check reports any the server does not have.
/// </summary>
public static class LiveTvActions
{
    /// <summary>The controller name (without the <c>Controller</c> suffix) of Jellyfin's live TV API.</summary>
    public const string Controller = "LiveTv";

    /// <summary>
    /// Gets the expected live TV actions and their rule.
    /// </summary>
    public static IReadOnlyList<(string Action, LiveTvRule Rule)> Expected { get; } =
    [
        ("GetLiveTvChannels", LiveTvRule.ChannelList),
        ("GetChannel", LiveTvRule.Channel),
        ("GetLiveTvPrograms", LiveTvRule.ProgramList),
        ("GetPrograms", LiveTvRule.ProgramListPosted),
        ("GetRecommendedPrograms", LiveTvRule.RecommendedPrograms),
        ("GetProgram", LiveTvRule.Program),
        ("GetRecordings", LiveTvRule.Recording),
        ("GetRecording", LiveTvRule.Recording),
        ("GetTimers", LiveTvRule.Recording),
        ("CreateTimer", LiveTvRule.Recording),
        ("GetSeriesTimers", LiveTvRule.Recording),
        ("CreateSeriesTimer", LiveTvRule.Recording),
        ("GetLiveRecordingFile", LiveTvRule.Recording),
    ];

    /// <summary>
    /// The rule for a live TV controller action: an expected one, or any action about recordings or timers (so those
    /// added in a later 12.x are refused too).
    /// </summary>
    /// <param name="controllerName">The controller name.</param>
    /// <param name="actionName">The action name.</param>
    /// <returns>The rule, or null when the action is not one of these.</returns>
    public static LiveTvRule? RuleFor(string controllerName, string actionName)
    {
        ArgumentNullException.ThrowIfNull(actionName);
        if (controllerName != Controller)
        {
            return null;
        }

        foreach (var (action, rule) in Expected)
        {
            if (action == actionName)
            {
                return rule;
            }
        }

        return actionName.Contains("Recording", StringComparison.Ordinal) || actionName.Contains("Timer", StringComparison.Ordinal)
            ? LiveTvRule.Recording
            : null;
    }
}
