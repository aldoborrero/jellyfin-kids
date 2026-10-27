using System;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// A bonus as stored (data-model.md "Bonus").
/// </summary>
/// <param name="Id">The bonus id; never reused.</param>
/// <param name="Day">The kid's day it was given for, <c>yyyy-MM-dd</c>.</param>
/// <param name="Minutes">The minutes, 1–240.</param>
/// <param name="GrantedBy">Who gave it, as recorded.</param>
/// <param name="CreatedAt">When it was given.</param>
public sealed record StoredBonus(long Id, string Day, int Minutes, string GrantedBy, DateTimeOffset CreatedAt);
