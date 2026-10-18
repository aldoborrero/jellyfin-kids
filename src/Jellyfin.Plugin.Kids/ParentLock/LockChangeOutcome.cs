namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// How a request to read or change the household lock ended.
/// </summary>
public enum LockChangeOutcome
{
    /// <summary>
    /// Done; the result carries the lock (null after a removal).
    /// </summary>
    Saved,

    /// <summary>
    /// The request is invalid; the result carries the errors, keyed by field.
    /// </summary>
    Invalid,

    /// <summary>
    /// The caller may not do this.
    /// </summary>
    Forbidden,

    /// <summary>
    /// An import while a lock is set; the result carries the current lock.
    /// </summary>
    Conflict,

    /// <summary>
    /// Too many wrong codes; the result carries the seconds to wait.
    /// </summary>
    Throttled,

    /// <summary>
    /// No lock is set.
    /// </summary>
    NoLock
}
