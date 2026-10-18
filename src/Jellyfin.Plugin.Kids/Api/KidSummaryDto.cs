using System;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// A kid in the list of kids: ids and revisions only.
/// </summary>
/// <param name="UserId">The Jellyfin user id.</param>
/// <param name="Revision">The profile revision.</param>
public record KidSummaryDto(Guid UserId, long Revision);
