using System;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// A sitting as read at one moment.
/// </summary>
/// <param name="UsedMs">Playing time in the sitting.</param>
/// <param name="RemainingMs">Time left before the limit, or null without a limit.</param>
/// <param name="BreakEndsAt">When the break after the limit ends, or null if the limit is not reached.</param>
/// <param name="StartedAt">When the sitting started, or null when there is none.</param>
public sealed record SittingState(long UsedMs, long? RemainingMs, DateTimeOffset? BreakEndsAt, DateTimeOffset? StartedAt);
