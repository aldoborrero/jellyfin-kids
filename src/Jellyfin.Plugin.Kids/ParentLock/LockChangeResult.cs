using System.Collections.Generic;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// The result of reading or changing the household lock.
/// </summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Lock">The stored lock after a save, or the current one on a conflict.</param>
/// <param name="Errors">The validation errors, keyed by field.</param>
/// <param name="RetryAfterSeconds">When throttled, the seconds to wait.</param>
public sealed record LockChangeResult(
    LockChangeOutcome Outcome,
    StoredLock? Lock = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    int? RetryAfterSeconds = null)
{
    /// <summary>
    /// A refusal.
    /// </summary>
    public static readonly LockChangeResult Forbidden = new(LockChangeOutcome.Forbidden);

    /// <summary>
    /// No lock is set.
    /// </summary>
    public static readonly LockChangeResult NoLock = new(LockChangeOutcome.NoLock);

    /// <summary>
    /// A validation error on one field.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    public static LockChangeResult Invalid(string field, string message)
        => new(LockChangeOutcome.Invalid, Errors: new Dictionary<string, string[]> { [field] = [message] });
}
