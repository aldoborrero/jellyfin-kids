using System;
using Jellyfin.Plugin.Kids.Allowance;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// One of today's bonuses, as clients see it (feature 13).
/// </summary>
/// <param name="Id">The bonus id, for <c>DELETE …/WatchTime/Bonus/{Id}</c>; never reused.</param>
/// <param name="Minutes">The minutes given.</param>
/// <param name="GrantedBy">Who gave it: a user name, "API key", or "{name} with a parent grant".</param>
/// <param name="CreatedAt">When it was given (UTC).</param>
/// <param name="UndoUntil">The last moment it can be undone (UTC).</param>
/// <param name="CanUndo">Whether it can be undone now, judged on the list's <c>ServerTime</c>.</param>
public record BonusDto(long Id, int Minutes, string GrantedBy, DateTime CreatedAt, DateTime UndoUntil, bool CanUndo)
{
    /// <summary>
    /// Maps a bonus with its undo state.
    /// </summary>
    /// <param name="state">The bonus.</param>
    /// <returns>The DTO.</returns>
    public static BonusDto From(BonusState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var bonus = state.Bonus;
        return new BonusDto(bonus.Id, bonus.Minutes, bonus.GrantedBy, bonus.CreatedAt.UtcDateTime, state.UndoUntil.UtcDateTime, state.CanUndo);
    }
}
