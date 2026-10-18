using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

public sealed class FakePickCatalog : IPickCatalog
{
    public List<PickCandidate> Items { get; } = [];

    public PickCandidate Add(string name, PickType type = PickType.BoxSet)
    {
        var item = new PickCandidate(Guid.NewGuid(), name, type);
        Items.Add(item);
        return item;
    }

    public IReadOnlyList<PickCandidate> Resolve(IEnumerable<Guid> ids)
        => [.. ids.SelectMany(id => Items.Where(i => i.Id == id))];

    public IReadOnlyList<PickCandidate> Search(string? term, int limit)
        => [.. Items
            .Where(i => term is null || i.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)];
}
