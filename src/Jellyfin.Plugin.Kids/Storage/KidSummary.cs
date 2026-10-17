using System;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// A kid and the revision of their profile.
/// </summary>
/// <param name="UserId">The Jellyfin user id.</param>
/// <param name="Revision">The profile revision.</param>
public sealed record KidSummary(Guid UserId, long Revision);
