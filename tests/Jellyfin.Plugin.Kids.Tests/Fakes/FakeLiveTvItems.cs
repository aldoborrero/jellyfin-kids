using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.LiveTv;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

public sealed class FakeLiveTvItems : ILiveTvItems
{
    public Dictionary<Guid, Guid> Channels { get; } = [];

    public Dictionary<string, string> OpenStreams { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Throw { get; set; }

    public Guid AddChannel()
    {
        var channel = Guid.NewGuid();
        Channels[channel] = channel;
        return channel;
    }

    public Guid AddProgram(Guid channel)
    {
        var program = Guid.NewGuid();
        Channels[program] = channel;
        return program;
    }

    public Guid? ChannelOf(Guid itemId) => Channels.TryGetValue(itemId, out var channel) ? channel : null;

    public bool IsOpen(string liveStreamId) => OpenStreams.ContainsKey(liveStreamId);

    public string? UniqueIdOf(string liveStreamId) => Throw ? throw new InvalidOperationException("boom") : OpenStreams.GetValueOrDefault(liveStreamId);
}
