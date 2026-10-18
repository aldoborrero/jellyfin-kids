using System;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// The answer to a code check.
/// </summary>
/// <param name="Valid">Whether the code was right.</param>
/// <param name="Grant">The grant issued for a right code.</param>
/// <param name="ExpiresAt">When the grant expires (UTC).</param>
/// <param name="RetryAfterSeconds">When throttled, the seconds to wait; the code was not checked.</param>
public sealed record VerifyOutcome(bool Valid, string? Grant = null, DateTime? ExpiresAt = null, int? RetryAfterSeconds = null)
{
    /// <summary>
    /// Not valid, with nothing more to say.
    /// </summary>
    public static readonly VerifyOutcome NotValid = new(false);

    /// <inheritdoc />
    public override string ToString() => $"VerifyOutcome {{ Valid = {Valid}, Grant = ***, ExpiresAt = {ExpiresAt}, RetryAfterSeconds = {RetryAfterSeconds} }}";
}
