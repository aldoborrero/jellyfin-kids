using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// The kinds of household parent lock the server stores, named as Wholphin's <c>ParentLockType</c>.
/// </summary>
/// <remarks>
/// Wholphin's third kind, <c>CHALLENGE</c> (an arithmetic question), stores no secret, so it stays on the device and
/// is never stored here.
/// </remarks>
public static class ParentLockType
{
    /// <summary>
    /// A code of digits.
    /// </summary>
    public const string Numeric = "NUMERIC";

    /// <summary>
    /// A sequence of remote buttons: D-pad, digits and extra buttons.
    /// </summary>
    public const string Remote = "REMOTE";

    /// <summary>
    /// Reads a lock type, ignoring case.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <param name="lockType">The lock type in its stored (uppercase) form.</param>
    /// <returns><c>true</c> if <paramref name="value"/> is a known lock type.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out string? lockType)
    {
        if (string.Equals(value, Numeric, StringComparison.OrdinalIgnoreCase))
        {
            lockType = Numeric;
            return true;
        }

        if (string.Equals(value, Remote, StringComparison.OrdinalIgnoreCase))
        {
            lockType = Remote;
            return true;
        }

        lockType = null;
        return false;
    }
}
