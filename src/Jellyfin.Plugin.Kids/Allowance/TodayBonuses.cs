using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// A kid's bonuses of the current day, as read at one moment (feature 13).
/// </summary>
/// <param name="Date">Today, under the "day starts at" rule.</param>
/// <param name="Now">When they were read; what <see cref="BonusState.CanUndo"/> was judged on.</param>
/// <param name="Bonuses">The bonuses, newest first.</param>
public sealed record TodayBonuses(DateOnly Date, DateTimeOffset Now, IReadOnlyList<BonusState> Bonuses);
