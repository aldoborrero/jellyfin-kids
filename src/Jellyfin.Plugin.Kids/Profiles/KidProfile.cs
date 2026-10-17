using System;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// A Jellyfin user marked as a kid, with their kids mode settings.
/// </summary>
/// <param name="UserId">The Jellyfin user id.</param>
/// <param name="Revision">The revision, 1 on creation and increased by one on every change.</param>
/// <param name="Settings">The kids mode settings.</param>
public sealed record KidProfile(Guid UserId, long Revision, KidProfileSettings Settings);
