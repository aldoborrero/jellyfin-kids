namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// A request to set, replace or import the household lock.
/// </summary>
/// <param name="LockType">The lock type, as sent.</param>
/// <param name="Code">The new code, when setting from a code.</param>
/// <param name="Verifier">The verifier, when importing.</param>
/// <param name="Current">The current code, required when the caller uses a parent grant.</param>
public sealed record LockUpdateInput(string? LockType, string? Code, string? Verifier, string? Current)
{
    /// <inheritdoc />
    public override string ToString() => $"LockUpdateInput {{ LockType = {LockType}, Code = ***, Verifier = ***, Current = *** }}";
}
