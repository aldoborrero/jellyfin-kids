using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// The result of a screen-time change.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Status">The kid's new status, when saved.</param>
/// <param name="Errors">The validation errors keyed by field, when invalid.</param>
public sealed record UsageResult(UsageOutcome Outcome, KidStatus? Status = null, IReadOnlyDictionary<string, string[]>? Errors = null);
