using System;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// A kid in the list of kids: ids, revisions and avatars, so a user switcher can show every kid at once.
/// </summary>
/// <param name="UserId">The Jellyfin user id.</param>
/// <param name="Revision">The profile revision.</param>
/// <param name="Avatar">The kid's avatar id; absent when the client should show the Jellyfin profile image.</param>
public record KidSummaryDto(Guid UserId, long Revision, string? Avatar = null);
