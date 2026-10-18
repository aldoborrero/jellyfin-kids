using System;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// The rules for a parent code, the same as Wholphin's <c>ParentLockKeys</c> and <c>PasscodeHasher</c>
/// (<c>ParentLockKeys.kt:31-37</c>, <c>PasscodeHasher.kt:14</c>), plus a maximum length that bounds the cost of a check.
/// </summary>
public static class ParentCode
{
    /// <summary>
    /// The shortest code allowed.
    /// </summary>
    public const int MinLength = 4;

    /// <summary>
    /// The longest code allowed.
    /// </summary>
    public const int MaxLength = 32;

    private const string Digits = "0123456789";

    // D-pad (U D L R), digits, then the extra buttons: colours r g y b, play/pause p, rewind w, fast-forward f,
    // channel + and -, info i
    private const string RemoteSymbols = "UDLR0123456789rgybpwf+-i";

    /// <summary>
    /// Checks a new code for a lock type. Symbols are compared exactly: <c>u</c> is not <c>U</c>, and <c>r</c> is the
    /// red button, not right.
    /// </summary>
    /// <param name="lockType">The lock type, <see cref="ParentLockType.Numeric"/> or <see cref="ParentLockType.Remote"/>.</param>
    /// <param name="code">The code.</param>
    /// <returns>Why the code is invalid, or null if it is valid.</returns>
    public static string? Validate(string lockType, string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length < MinLength || code.Length > MaxLength)
        {
            return $"Must be {MinLength} to {MaxLength} symbols.";
        }

        var allowed = string.Equals(lockType, ParentLockType.Numeric, StringComparison.Ordinal) ? Digits : RemoteSymbols;
        foreach (var symbol in code)
        {
            if (!allowed.Contains(symbol, StringComparison.Ordinal))
            {
                return lockType == ParentLockType.Numeric
                    ? "Digits only."
                    : "Only U D L R, 0-9 and r g y b p w f + - i.";
            }
        }

        return null;
    }
}
