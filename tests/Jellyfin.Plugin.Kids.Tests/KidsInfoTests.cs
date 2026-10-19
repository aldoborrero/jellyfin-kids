using Jellyfin.Plugin.Kids.Api;
using Jellyfin.Plugin.Kids.Enforcement;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class KidsInfoTests
{
    [Fact]
    public void SupportedFeatures_AddEnforcementAndSittingAfterTheEarlierOnes()
    {
        Assert.Equal(["profiles", "parentLock", "allowance", "sessionLimit", "enforcement", "sitting"], KidsInfo.SupportedFeatures);
    }

    [Fact]
    public void EnforcementInfo_IsPendingUntilTheCheckRan()
    {
        var coverage = new GateCoverage();
        Assert.Equal("pending", EnforcementInfo.From(coverage).Gate);
        Assert.Empty(EnforcementInfo.From(coverage).Missing);

        coverage.MarkChecked();
        var degraded = EnforcementInfo.From(coverage);
        Assert.Equal("degraded", degraded.Gate);
        Assert.Contains("MediaInfo.GetPlaybackInfo", degraded.Missing);

        foreach (var (controller, action, kind) in GateActions.Expected)
        {
            coverage.Add(controller, action, kind, true);
        }

        Assert.Equal("ok", EnforcementInfo.From(coverage).Gate);
        Assert.Empty(EnforcementInfo.From(coverage).Missing);
    }
}
