using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// A home section with its visibility; its position in the list is its order.
/// </summary>
/// <param name="Section">The section.</param>
/// <param name="Visible">Whether the section is shown.</param>
public sealed record HomeSectionEntry(HomeSection Section, bool Visible = true)
{
    /// <summary>
    /// Gets every section, visible, in the default order.
    /// </summary>
    public static IReadOnlyList<HomeSectionEntry> Default { get; } =
        [.. Enum.GetValues<HomeSection>().Select(s => new HomeSectionEntry(s))];

    /// <summary>
    /// Appends the sections missing from <paramref name="entries"/>, visible, in the default order. Duplicates are
    /// kept; validation rejects them.
    /// </summary>
    /// <param name="entries">The sections in their chosen order.</param>
    /// <returns>The complete list of sections.</returns>
    public static IReadOnlyList<HomeSectionEntry> Normalize(IEnumerable<HomeSectionEntry> entries)
    {
        var list = entries.ToList();
        list.AddRange(Default.Where(d => list.TrueForAll(e => e.Section != d.Section)));
        return list;
    }
}
