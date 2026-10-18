using System;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// The household parent lock as stored.
/// </summary>
/// <param name="LockType">The lock type, <see cref="ParentLockType.Numeric"/> or <see cref="ParentLockType.Remote"/>.</param>
/// <param name="Verifier">The verifier; never the code.</param>
/// <param name="ChangedAt">When the lock was last set, replaced or imported (UTC).</param>
public sealed record StoredLock(string LockType, string Verifier, DateTime ChangedAt);
