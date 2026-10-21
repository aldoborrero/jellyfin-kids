using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

public sealed class FakeLiveTvChannelCatalog : ILiveTvChannelCatalog
{
    public List<LiveTvChannelInfo> Items { get; } = [];

    public LiveTvChannelInfo Add(string name, string? number = null)
    {
        var channel = new LiveTvChannelInfo(Guid.NewGuid(), name, number, null, false);
        Items.Add(channel);
        return channel;
    }

    public IReadOnlyList<LiveTvChannelInfo> Resolve(IEnumerable<Guid> ids)
        => [.. ids.SelectMany(id => Items.Where(i => i.Id == id))];

    public IReadOnlyList<LiveTvChannelInfo> List() => Items;
}
