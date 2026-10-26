using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// The avatars a kid profile can name. The server holds only the id; the pictures belong to the clients.
/// </summary>
public static class KidAvatars
{
    /// <summary>
    /// Gets the avatar ids, lowercase. Adding one is additive (raises the API minor version).
    /// </summary>
    public static IReadOnlyList<string> Ids { get; } = ["leo", "max", "nico", "lucy", "sara", "mia"];

    /// <summary>
    /// Normalises an avatar as sent by a client: trimmed and matched case-insensitively; blank means none.
    /// </summary>
    /// <param name="value">The value as sent.</param>
    /// <param name="id">The lowercase id, or null for none.</param>
    /// <returns><c>true</c> if the value is none or a known id.</returns>
    public static bool TryNormalize(string? value, out string? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        id = Ids.FirstOrDefault(known => string.Equals(known, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return id is not null;
    }
}
