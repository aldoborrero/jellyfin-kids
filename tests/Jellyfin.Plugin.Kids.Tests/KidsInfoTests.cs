using Jellyfin.Plugin.Kids.Api;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class KidsInfoTests
{
    [Fact]
    public void SupportedFeatures_AddAvatarAfterTheEarlierOnes()
    {
        Assert.Equal(["profiles", "parentLock", "allowance", "sessionLimit", "enforcement", "sitting", "liveTv", "watchTimeReset", "avatar"], KidsInfo.SupportedFeatures);
    }

    [Fact]
    public void ApiVersion_StaysOneWithMinorVersionOne()
    {
        Assert.Equal(1, KidsInfo.CurrentApiVersion);
        Assert.Equal(1, KidsInfo.CurrentApiMinorVersion);
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

    [Fact]
    public void LiveTvInfo_ReportsTheLiveTvRoutesSeparately()
    {
        var coverage = new GateCoverage();
        Assert.Equal("pending", EnforcementInfo.FromLiveTv(coverage).Gate);

        coverage.MarkChecked();
        Assert.Contains("LiveTv.GetLiveTvChannels", EnforcementInfo.FromLiveTv(coverage).Missing);

        foreach (var (action, _) in LiveTvActions.Expected)
        {
            coverage.AddLiveTv(action);
        }

        var info = EnforcementInfo.FromLiveTv(coverage);
        Assert.Equal("ok", info.Gate);
        Assert.Empty(info.Missing);
        Assert.Equal("degraded", EnforcementInfo.From(coverage).Gate);
    }
}
