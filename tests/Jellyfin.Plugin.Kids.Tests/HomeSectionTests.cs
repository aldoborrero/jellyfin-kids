using System.Linq;
using Jellyfin.Plugin.Kids.Profiles;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class HomeSectionTests
{
    [Fact]
    public void Default_IsAllSectionsVisibleInDefaultOrder()
    {
        Assert.Equal(
            [HomeSection.KeepWatching, HomeSection.Picked, HomeSection.Libraries, HomeSection.New, HomeSection.Favourites],
            HomeSectionEntry.Default.Select(e => e.Section));
        Assert.All(HomeSectionEntry.Default, e => Assert.True(e.Visible));
    }

    [Fact]
    public void Normalize_AppendsMissingSectionsVisibleInDefaultOrder()
    {
        var normalized = HomeSectionEntry.Normalize([new(HomeSection.New, false), new(HomeSection.Picked, true)]);

        Assert.Equal(
            [
                new HomeSectionEntry(HomeSection.New, false),
                new HomeSectionEntry(HomeSection.Picked, true),
                new HomeSectionEntry(HomeSection.KeepWatching, true),
                new HomeSectionEntry(HomeSection.Libraries, true),
                new HomeSectionEntry(HomeSection.Favourites, true)
            ],
            normalized);
    }

    [Fact]
    public void Normalize_KeepsGivenOrderWhenComplete()
    {
        var reversed = HomeSectionEntry.Default.Reverse().ToList();

        Assert.Equal(reversed, HomeSectionEntry.Normalize(reversed));
    }
}
