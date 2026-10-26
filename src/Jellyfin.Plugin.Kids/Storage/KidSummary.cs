using System;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// A kid, the revision of their profile and their avatar.
/// </summary>
/// <param name="UserId">The Jellyfin user id.</param>
/// <param name="Revision">The profile revision.</param>
/// <param name="Avatar">The avatar id, or null for none.</param>
public sealed record KidSummary(Guid UserId, long Revision, string? Avatar = null);
