using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// Parent code verifiers in Wholphin's format, <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;saltHex&gt;$&lt;hashHex&gt;</c>.
/// </summary>
/// <remarks>
/// Wholphin's <c>PasscodeHasher.kt</c> derives one PBKDF2-HMAC-SHA256 block over the UTF-8 code (RFC 8018 with a
/// 32-byte output), which is what <see cref="Rfc2898DeriveBytes.Pbkdf2(byte[], byte[], int, HashAlgorithmName, int)"/>
/// computes, so verifiers made on either side check on the other.
/// </remarks>
public static class Verifier
{
    /// <summary>
    /// The first part of every verifier.
    /// </summary>
    public const string Prefix = "pbkdf2-sha256";

    /// <summary>
    /// The iterations used for new verifiers, as in Wholphin.
    /// </summary>
    public const int Iterations = 120_000;

    /// <summary>
    /// The salt size of new verifiers, in bytes.
    /// </summary>
    public const int SaltBytes = 16;

    /// <summary>
    /// The hash size, in bytes.
    /// </summary>
    public const int HashBytes = 32;

    /// <summary>
    /// The fewest iterations an imported verifier may use.
    /// </summary>
    public const int MinImportIterations = 10_000;

    /// <summary>
    /// The most iterations an imported verifier may use, which bounds the cost of every later check.
    /// </summary>
    public const int MaxImportIterations = 1_000_000;

    /// <summary>
    /// The shortest salt an imported verifier may use, in bytes.
    /// </summary>
    public const int MinImportSaltBytes = 16;

    /// <summary>
    /// The longest salt an imported verifier may use, in bytes.
    /// </summary>
    public const int MaxImportSaltBytes = 64;

    /// <summary>
    /// Creates a verifier for a code, with a fresh random salt.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <returns>The verifier.</returns>
    public static string Create(string code) => Create(code, RandomNumberGenerator.GetBytes(SaltBytes), Iterations);

    /// <summary>
    /// Checks a code against a verifier in constant time. A malformed verifier never verifies.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="verifier">The verifier.</param>
    /// <returns><c>true</c> if the code matches.</returns>
    public static bool Verify(string code, string verifier)
    {
        if (!TryParse(verifier, out var iterations, out var salt, out var expected) || expected.Length != HashBytes)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Checks that a verifier sent for import is well formed and within the accepted ranges.
    /// </summary>
    /// <param name="verifier">The verifier.</param>
    /// <returns>Why the verifier is refused, or null if it can be imported.</returns>
    public static string? ValidateForImport(string? verifier)
    {
        if (verifier is null || !TryParse(verifier, out var iterations, out var salt, out var hash))
        {
            return $"Must be {Prefix}$<iterations>$<saltHex>$<hashHex>.";
        }

        if (iterations < MinImportIterations || iterations > MaxImportIterations)
        {
            return $"Iterations must be between {MinImportIterations} and {MaxImportIterations}.";
        }

        if (salt.Length < MinImportSaltBytes || salt.Length > MaxImportSaltBytes)
        {
            return $"The salt must be {MinImportSaltBytes} to {MaxImportSaltBytes} bytes.";
        }

        return hash.Length == HashBytes ? null : $"The hash must be {HashBytes} bytes.";
    }

    /// <summary>
    /// Creates a verifier with a given salt and iteration count.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="salt">The salt.</param>
    /// <param name="iterations">The iterations.</param>
    /// <returns>The verifier, with lowercase hex as Wholphin writes it.</returns>
    internal static string Create(string code, byte[] salt, int iterations)
    {
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join(
            '$',
            Prefix,
            iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexStringLower(salt),
            Convert.ToHexStringLower(hash));
    }

    private static bool TryParse(string verifier, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        var parts = verifier.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations <= 0)
        {
            return false;
        }

        try
        {
            // Either case, like Wholphin's toInt(16)
            salt = Convert.FromHexString(parts[2]);
            hash = Convert.FromHexString(parts[3]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
