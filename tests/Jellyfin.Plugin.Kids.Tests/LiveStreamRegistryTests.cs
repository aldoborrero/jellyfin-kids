using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class LiveStreamRegistryTests
{
    private readonly LiveStreamRegistry _registry = new();
    private readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Recorded_StreamIsFoundByBothIds_IgnoringCase()
    {
        var channel = Guid.NewGuid();
        _open.Add("live-1");

        _registry.Record("live-1", "unique-1", channel, _open.Contains);

        Assert.True(_registry.TryGetChannel("LIVE-1", out var byId));
        Assert.Equal(channel, byId);
        Assert.True(_registry.TryGetChannel("unique-1", out var byUnique));
        Assert.Equal(channel, byUnique);
        Assert.False(_registry.TryGetChannel("live-2", out _));
        Assert.False(_registry.TryGetChannel(string.Empty, out _));
    }

    [Fact]
    public void Record_ForgetsStreamsThatClosed()
    {
        _open.Add("live-1");
        _registry.Record("live-1", "unique-1", Guid.NewGuid(), _open.Contains);
        _open.Remove("live-1");
        _open.Add("live-2");

        _registry.Record("live-2", null, Guid.NewGuid(), _open.Contains);

        Assert.False(_registry.TryGetChannel("live-1", out _));
        Assert.False(_registry.TryGetChannel("unique-1", out _));
        Assert.True(_registry.TryGetChannel("live-2", out _));
        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void Record_KeepsTheStreamBeingRecordedEvenIfNotOpenYet()
    {
        var channel = Guid.NewGuid();

        _registry.Record("live-1", null, channel, _ => false);

        Assert.True(_registry.TryGetChannel("live-1", out var found));
        Assert.Equal(channel, found);
    }
}
