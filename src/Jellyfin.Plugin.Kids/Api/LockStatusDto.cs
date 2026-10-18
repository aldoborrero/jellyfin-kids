using System;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// What any signed-in user may know about the household lock. Both fields are omitted when no lock is set.
/// </summary>
/// <param name="LockType">The lock type, <c>NUMERIC</c> or <c>REMOTE</c>.</param>
/// <param name="ChangedAt">When the lock was last set, replaced or imported (UTC).</param>
public record LockStatusDto(string? LockType, DateTime? ChangedAt);
