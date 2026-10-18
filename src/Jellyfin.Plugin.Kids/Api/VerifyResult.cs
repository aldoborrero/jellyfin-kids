using System;
using Jellyfin.Plugin.Kids.ParentLock;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The answer to <c>POST /Kids/Parent/Verify</c>, always with status 200.
/// </summary>
/// <param name="Valid">Whether the code was right.</param>
/// <param name="Grant">A right code's grant, to send in <c>X-Kids-Grant</c> from this device.</param>
/// <param name="ExpiresAt">When the grant expires (UTC).</param>
/// <param name="RetryAfterSeconds">When throttled, the seconds to wait; the code was not checked.</param>
public record VerifyResult(bool Valid, string? Grant, DateTime? ExpiresAt, int? RetryAfterSeconds)
{
    /// <summary>
    /// Converts the service answer.
    /// </summary>
    /// <param name="outcome">The service answer.</param>
    /// <returns>The response.</returns>
    public static VerifyResult From(VerifyOutcome outcome)
    {
        System.ArgumentNullException.ThrowIfNull(outcome);
        return new VerifyResult(outcome.Valid, outcome.Grant, outcome.ExpiresAt, outcome.RetryAfterSeconds);
    }

    /// <inheritdoc />
    public override string ToString() => $"VerifyResult {{ Valid = {Valid}, Grant = ***, ExpiresAt = {ExpiresAt}, RetryAfterSeconds = {RetryAfterSeconds} }}";
}
