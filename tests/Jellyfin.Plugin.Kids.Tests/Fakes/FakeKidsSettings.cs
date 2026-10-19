using System;
using Jellyfin.Plugin.Kids.Allowance;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

public sealed class FakeKidsSettings : IKidsSettings
{
    public int DayStartsAtHour { get; set; }

    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

    public bool EnforcementEnabled { get; set; }

    public TimeSpan SittingBreak { get; set; } = TimeSpan.FromMinutes(15);
}
