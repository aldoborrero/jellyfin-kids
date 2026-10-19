using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.Enforcement;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

public sealed class FakePlaybackGate : IPlaybackGate
{
    public bool IsEnabled { get; set; } = true;

    public HashSet<Guid> Kids { get; } = [];

    public Dictionary<Guid, Denial> Denied { get; } = [];

    public Dictionary<string, Guid> Devices { get; } = [];

    public bool Throw { get; set; }

    public bool IsKid(Guid userId) => Throw ? throw new InvalidOperationException("boom") : Kids.Contains(userId);

    public bool TryGetDenial(Guid kid, DateTimeOffset now, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out Denial denial) => Denied.TryGetValue(kid, out denial);

    public Guid KidForDevice(string deviceId) => Devices.GetValueOrDefault(deviceId);
}
