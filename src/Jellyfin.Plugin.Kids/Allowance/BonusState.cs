using System;
using Jellyfin.Plugin.Kids.Storage;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// A bonus with its undo state (feature 13).
/// </summary>
/// <param name="Bonus">The bonus.</param>
/// <param name="UndoUntil">The last moment it can be undone.</param>
/// <param name="CanUndo">Whether it can be undone now.</param>
public sealed record BonusState(StoredBonus Bonus, DateTimeOffset UndoUntil, bool CanUndo);
