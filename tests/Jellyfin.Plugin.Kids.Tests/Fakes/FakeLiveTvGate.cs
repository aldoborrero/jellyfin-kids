using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.LiveTv;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

public sealed class FakeLiveTvGate : ILiveTvGate
{
    public HashSet<Guid> Kids { get; } = [];

    public Dictionary<Guid, HashSet<Guid>> Allowed { get; } = [];

    public Dictionary<string, Guid> Devices { get; } = [];

    public bool Throw { get; set; }

    public bool IsKid(Guid userId) => Throw ? throw new InvalidOperationException("boom") : Kids.Contains(userId);

    public Guid KidForDevice(string deviceId) => Devices.GetValueOrDefault(deviceId);

    public IReadOnlySet<Guid> AllowedChannels(Guid kid) => Allowed.TryGetValue(kid, out var allowed) ? allowed : [];
}
