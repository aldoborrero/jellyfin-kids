using System;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Why a kid may not play now, and until when if nothing changes (data-model.md "Denial").
/// </summary>
/// <param name="Reason">Why.</param>
/// <param name="Until">When it ends: the next day start, the end of the break, or null for allowed hours.</param>
public sealed record Denial(DenialReason Reason, DateTimeOffset? Until)
{
    /// <summary>
    /// Gets the reason as clients see it: <c>timeUp</c>, <c>outsideSchedule</c> or <c>sessionLimit</c>.
    /// </summary>
    public string Code => Reason switch
    {
        DenialReason.TimeUp => "timeUp",
        DenialReason.OutsideSchedule => "outsideSchedule",
        _ => "sessionLimit"
    };
}
