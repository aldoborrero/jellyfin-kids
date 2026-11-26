using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Stats;

/// <summary>
/// When a kid watches (feature 22): seconds per hour of the day, server local time, over the range.
/// </summary>
/// <param name="Weekdays">24 values, Monday to Friday together.</param>
/// <param name="Weekend">24 values, Saturday and Sunday together.</param>
public sealed record HourGrid(IReadOnlyList<int> Weekdays, IReadOnlyList<int> Weekend);
