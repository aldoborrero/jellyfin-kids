using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

public sealed class FakeUserDirectory : IKidsUserDirectory
{
    public HashSet<Guid> Users { get; } = [];

    public bool UserExists(Guid userId) => Users.Contains(userId);

    public IReadOnlyCollection<Guid> GetAllUserIds() => [.. Users];
}
