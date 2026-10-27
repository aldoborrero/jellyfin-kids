using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.Storage;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// The result of a screen-time change.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Status">The kid's new status, when saved.</param>
/// <param name="Errors">The validation errors keyed by field, when invalid.</param>
/// <param name="Bonus">The bonus given or undone (feature 13), when there was one.</param>
/// <param name="BonusUndoUntil">Until when that bonus can be undone, for a bonus just given.</param>
public sealed record UsageResult(
    UsageOutcome Outcome,
    KidStatus? Status = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    StoredBonus? Bonus = null,
    DateTimeOffset? BonusUndoUntil = null);
