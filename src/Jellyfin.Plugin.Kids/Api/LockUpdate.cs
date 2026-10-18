using Jellyfin.Plugin.Kids.ParentLock;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The body of <c>PUT /Kids/Parent/Lock</c>: a lock type with either a new code or a verifier to import.
/// </summary>
public class LockUpdate
{
    /// <summary>
    /// Gets or sets the lock type, <c>NUMERIC</c> or <c>REMOTE</c> (any case).
    /// </summary>
    public string? LockType { get; set; }

    /// <summary>
    /// Gets or sets the new code.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Gets or sets a verifier to import, in Wholphin's format; only when no lock is set.
    /// </summary>
    public string? Verifier { get; set; }

    /// <summary>
    /// Gets or sets the current code, required when the caller uses a parent grant instead of an administrator.
    /// </summary>
    public string? Current { get; set; }

    /// <summary>
    /// Converts the body to the service input.
    /// </summary>
    /// <returns>The input.</returns>
    public LockUpdateInput ToInput() => new(LockType, Code, Verifier, Current);

    /// <inheritdoc />
    public override string ToString() => $"LockUpdate {{ LockType = {LockType}, Code = ***, Verifier = ***, Current = *** }}";
}
