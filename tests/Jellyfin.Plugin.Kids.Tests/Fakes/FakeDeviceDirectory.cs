using System.Collections.Generic;
using Jellyfin.Plugin.Kids.ParentLock;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

public sealed class FakeDeviceDirectory : IDeviceDirectory
{
    public Dictionary<string, string> DevicesByToken { get; } = [];

    public string? GetRegisteredDeviceId(string token) => DevicesByToken.GetValueOrDefault(token);
}
