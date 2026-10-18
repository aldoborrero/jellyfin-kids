namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The household lock for a device to cache, so it can unlock while the server is unreachable.
/// </summary>
/// <param name="LockType">The lock type, <c>NUMERIC</c> or <c>REMOTE</c>.</param>
/// <param name="Verifier">The verifier, in Wholphin's <c>pbkdf2-sha256$iterations$saltHex$hashHex</c> format.</param>
public record VerifierDto(string LockType, string Verifier);
