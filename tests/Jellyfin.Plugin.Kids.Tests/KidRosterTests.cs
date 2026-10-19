using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class KidRosterTests
{
    [Fact]
    public void New_IsEmptyAndNotLoaded()
    {
        var roster = new KidRoster();

        Assert.False(roster.IsLoaded);
        Assert.False(roster.Contains(Guid.NewGuid()));
    }

    [Fact]
    public void AddAndRemove()
    {
        var roster = new KidRoster();
        var kid = Guid.NewGuid();

        roster.Add(kid);
        Assert.True(roster.Contains(kid));

        roster.Remove(kid);
        Assert.False(roster.Contains(kid));
    }

    [Fact]
    public void Replace_SetsTheWholeSetAndMarksLoaded()
    {
        var roster = new KidRoster();
        var old = Guid.NewGuid();
        var kid = Guid.NewGuid();
        roster.Add(old);

        roster.Replace([kid]);

        Assert.True(roster.IsLoaded);
        Assert.True(roster.Contains(kid));
        Assert.False(roster.Contains(old));
    }

    [Fact]
    public async Task ConcurrentAddAndContains_AreSafe()
    {
        var roster = new KidRoster();
        var ids = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(
            Task.Run(() => ids.ForEach(roster.Add)),
            Task.Run(() => ids.ForEach(id => roster.Contains(id))));

        Assert.All(ids, id => Assert.True(roster.Contains(id)));
    }
}
