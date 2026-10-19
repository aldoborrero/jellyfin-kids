namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Why a kid may not play now (data-model.md "DenialReason"), in the order a kid's denial reports them.
/// </summary>
public enum DenialReason
{
    /// <summary>Outside the kid's Jellyfin allowed hours: <c>outsideSchedule</c>.</summary>
    OutsideSchedule,

    /// <summary>Today's allowance plus bonus is used up: <c>timeUp</c>.</summary>
    TimeUp,

    /// <summary>The sitting reached its limit and the break is not over: <c>sessionLimit</c>.</summary>
    SessionLimit
}
