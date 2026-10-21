using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.LiveTv;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class GateConventionTests
{
    private static readonly MethodInfo AnyMethod = typeof(GateConventionTests).GetMethod(nameof(Placeholder), BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void GuardsEveryExpectedActionByName()
    {
        var application = new ApplicationModel();
        foreach (var group in GateActions.Expected.GroupBy(e => e.Controller))
        {
            AddController(application, group.Key, group.Select(e => (e.Action, Array.Empty<object>())).ToArray());
        }

        var coverage = new GateCoverage();
        new PlaybackGateConvention(coverage).Apply(application);

        foreach (var action in application.Controllers.SelectMany(c => c.Actions))
        {
            Assert.Single(action.Filters.OfType<ServiceFilterAttribute>(), f => f.ServiceType == typeof(KidsPlaybackFilter));
        }

        Assert.Empty(coverage.Missing);
        Assert.Equal(GateActions.Expected.Count, coverage.Matched.Count);
        Assert.All(coverage.Matched, m => Assert.True(m.ByName));
        var playbackInfo = application.Controllers.Single(c => c.ControllerName == "MediaInfo").Actions.Single(a => a.ActionName == "GetPlaybackInfo");
        Assert.Equal(GateKind.Negotiate, playbackInfo.Properties[typeof(GateKind)]);
        var open = application.Controllers.Single(c => c.ControllerName == "MediaInfo").Actions.Single(a => a.ActionName == "OpenLiveStream");
        Assert.Equal(GateKind.Open, open.Properties[typeof(GateKind)]);
    }

    [Fact]
    public void GuardsRenamedMediaActionsByAttribute()
    {
        var application = new ApplicationModel();
        AddController(
            application,
            "Videos",
            ("GetVideoStreamRenamed", [new ProducesVideoFileAttribute()]),
            ("GetAudioThing", [new ProducesFileAttribute("audio/*")]),
            ("GetPlaylist", [new ProducesPlaylistFileAttribute()]),
            ("GetText", [new ProducesFileAttribute("text/*")]),
            ("GetPlain", []));

        var coverage = new GateCoverage();
        new PlaybackGateConvention(coverage).Apply(application);

        var guarded = application.Controllers.Single().Actions.Where(a => a.Filters.Count > 0).Select(a => a.ActionName).ToList();
        Assert.Equal(["GetVideoStreamRenamed", "GetAudioThing", "GetPlaylist"], guarded);
        Assert.Equal(["Videos.GetVideoStreamRenamed", "Videos.GetAudioThing", "Videos.GetPlaylist"], coverage.Matched.Select(m => m.Controller + "." + m.Action));
        Assert.All(coverage.Matched, m => Assert.False(m.ByName));
        Assert.All(coverage.Matched, m => Assert.Equal(GateKind.Deliver, m.Kind));
        Assert.Contains("Videos.GetVideoStream", coverage.Missing);
        Assert.Equal(GateActions.Expected.Count, coverage.Missing.Count);
    }

    [Theory]
    [InlineData("Subtitle")]
    [InlineData("Trickplay")]
    [InlineData("VideoAttachments")]
    [InlineData("Playstate")]
    public void SkipsControllersThatAreNotPlayback(string controller)
    {
        var application = new ApplicationModel();
        AddController(application, controller, ("GetSomething", [new ProducesVideoFileAttribute()]), ("GetList", [new ProducesPlaylistFileAttribute()]));

        new PlaybackGateConvention(new GateCoverage()).Apply(application);

        Assert.All(application.Controllers.Single().Actions, a => Assert.Empty(a.Filters));
    }

    [Fact]
    public void SkipsThePluginsOwnControllers()
    {
        var application = new ApplicationModel();
        var controller = new ControllerModel(typeof(PlaybackGateConvention).GetTypeInfo(), []) { ControllerName = "MediaInfo", Application = application };
        controller.Actions.Add(new ActionModel(AnyMethod, []) { ActionName = "GetPlaybackInfo", Controller = controller });
        application.Controllers.Add(controller);

        new PlaybackGateConvention(new GateCoverage()).Apply(application);

        Assert.Empty(controller.Actions.Single().Filters);
    }

    [Fact]
    public void Coverage_IsPendingUntilChecked()
    {
        var coverage = new GateCoverage();
        Assert.Equal(GateCoverage.CheckState.Pending, coverage.State);

        coverage.MarkChecked();
        Assert.Equal(GateCoverage.CheckState.Degraded, coverage.State);

        foreach (var (controller, action, kind) in GateActions.Expected)
        {
            coverage.Add(controller, action, kind, true);
        }

        Assert.Equal(GateCoverage.CheckState.Ok, coverage.State);
    }

    [Fact]
    public void LiveTv_AttachesTheLiveTvFilterToPlaybackBrowseAndRecordingActions()
    {
        var application = new ApplicationModel();
        AddController(application, "MediaInfo", ("GetPlaybackInfo", []), ("OpenLiveStream", []));
        AddController(application, "LiveTv", [.. LiveTvActions.Expected.Select(e => (e.Action, Array.Empty<object>())), ("GetLiveStreamFile", []), ("DeleteRecording", []), ("UpdateSeriesTimer", []), ("GetGuideInfo", [])]);

        var coverage = new GateCoverage();
        new PlaybackGateConvention(coverage).Apply(application);

        LiveTvRule? RuleOf(string controller, string action)
        {
            var model = application.Controllers.Single(c => c.ControllerName == controller).Actions.Single(a => a.ActionName == action);
            var attached = model.Filters.OfType<ServiceFilterAttribute>().Count(f => f.ServiceType == typeof(KidsLiveTvFilter));
            Assert.InRange(attached, 0, 1);
            return attached == 1 ? (LiveTvRule)model.Properties[typeof(LiveTvRule)]! : null;
        }

        Assert.Equal(LiveTvRule.Playback, RuleOf("MediaInfo", "GetPlaybackInfo"));
        Assert.Equal(LiveTvRule.Playback, RuleOf("MediaInfo", "OpenLiveStream"));
        Assert.Equal(LiveTvRule.Playback, RuleOf("LiveTv", "GetLiveStreamFile"));
        Assert.Equal(LiveTvRule.Recording, RuleOf("LiveTv", "GetLiveRecordingFile"));
        Assert.Equal(LiveTvRule.Recording, RuleOf("LiveTv", "DeleteRecording"));
        Assert.Equal(LiveTvRule.Recording, RuleOf("LiveTv", "UpdateSeriesTimer"));
        Assert.Equal(LiveTvRule.ChannelList, RuleOf("LiveTv", "GetLiveTvChannels"));
        Assert.Equal(LiveTvRule.ProgramListPosted, RuleOf("LiveTv", "GetPrograms"));
        Assert.Null(RuleOf("LiveTv", "GetGuideInfo"));
        Assert.Empty(coverage.LiveTvMissing);
        coverage.MarkChecked();
        Assert.Equal(GateCoverage.CheckState.Ok, coverage.LiveTvState);
    }

    [Fact]
    public void LiveTv_MissingActionsAreReported()
    {
        var application = new ApplicationModel();
        AddController(application, "LiveTv", ("GetLiveTvChannels", []));

        var coverage = new GateCoverage();
        new PlaybackGateConvention(coverage).Apply(application);
        coverage.MarkChecked();

        Assert.Equal(GateCoverage.CheckState.Degraded, coverage.LiveTvState);
        Assert.Contains("LiveTv.GetPrograms", coverage.LiveTvMissing);
        Assert.DoesNotContain("LiveTv.GetLiveTvChannels", coverage.LiveTvMissing);
    }

    [Fact]
    public void LiveTv_OtherControllersActionsAreNotLiveTvRules()
    {
        Assert.Null(LiveTvActions.RuleFor("Items", "GetRecordingThing"));
        Assert.Equal(LiveTvRule.Recording, LiveTvActions.RuleFor("LiveTv", "GetRecordingGroups"));
        Assert.Null(LiveTvActions.RuleFor("LiveTv", "GetLiveTvInfo"));
    }

    private static void Placeholder()
    {
    }

    private static void AddController(ApplicationModel application, string name, params (string Action, object[] Attributes)[] actions)
    {
        var controller = new ControllerModel(typeof(GateConventionTests).GetTypeInfo(), []) { ControllerName = name, Application = application };
        foreach (var (action, attributes) in actions)
        {
            controller.Actions.Add(new ActionModel(AnyMethod, attributes) { ActionName = action, Controller = controller });
        }

        application.Controllers.Add(controller);
    }
}

// Stand-ins for Jellyfin.Api's attributes, which the convention matches by type name
#pragma warning disable SA1402, SA1649, CA1813, CA1019
[AttributeUsage(AttributeTargets.Method)]
public class ProducesFileAttribute(params string[] contentTypes) : Attribute
{
    public string[] ContentTypes { get; } = contentTypes;
}

public sealed class ProducesVideoFileAttribute() : ProducesFileAttribute("video/*");

public sealed class ProducesPlaylistFileAttribute() : ProducesFileAttribute("application/x-mpegURL");
#pragma warning restore SA1402, SA1649, CA1813, CA1019
