using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

public sealed class FakeLiveTvChannelCatalog : ILiveTvChannelCatalog
{
    public List<LiveTvChannelInfo> Items { get; } = [];

    // Channels Jellyfin hides from a user, as its parental control would
    public Dictionary<Guid, HashSet<Guid>> HiddenFrom { get; } = [];

    public LiveTvChannelInfo Add(string name, string? number = null, string? rating = null)
    {
        var channel = new LiveTvChannelInfo(Guid.NewGuid(), name, number, null, false, rating);
        Items.Add(channel);
        return channel;
    }

    public IReadOnlyList<LiveTvChannelInfo> Resolve(IEnumerable<Guid> ids)
        => [.. ids.SelectMany(id => Items.Where(i => i.Id == id))];

    public IReadOnlyList<LiveTvChannelInfo> List() => Items;

    public IReadOnlySet<Guid> VisibleTo(Guid userId)
        => Items.Select(i => i.Id).Where(id => !HiddenFrom.TryGetValue(userId, out var hidden) || !hidden.Contains(id)).ToHashSet();
}
