using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// How a kid is doing today (data-model.md "KidStatus"). Not stored.
/// </summary>
/// <param name="Date">The day, under the "day starts at" rule.</param>
/// <param name="UsedSeconds">Time used today, rounded down to the second.</param>
/// <param name="AllowanceSeconds">The daily allowance, or null when unlimited.</param>
/// <param name="BonusSeconds">The bonus added today.</param>
/// <param name="RemainingSeconds">Allowance plus bonus minus used, never negative; null when unlimited.</param>
/// <param name="TimeUp">Whether an allowance is set and used has reached allowance plus bonus.</param>
/// <param name="ServerTime">The server's current time.</param>
/// <param name="NextDayStartsAt">When the next day starts.</param>
/// <param name="WarnAtSeconds">The remaining times at which clients should warn.</param>
public sealed record KidStatus(
    DateOnly Date,
    long UsedSeconds,
    long? AllowanceSeconds,
    long BonusSeconds,
    long? RemainingSeconds,
    bool TimeUp,
    DateTimeOffset ServerTime,
    DateTimeOffset NextDayStartsAt,
    IReadOnlyList<int> WarnAtSeconds);
