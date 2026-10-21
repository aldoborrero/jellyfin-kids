using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.LiveTv;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class KidsLiveTvFilterTests
{
    private static readonly Guid Kid = Guid.NewGuid();
    private static readonly Guid Adult = Guid.NewGuid();

    private readonly FakeLiveTvGate _gate = new();
    private readonly FakeLiveTvItems _items = new();
    private readonly LiveStreamRegistry _streams = new();
    private readonly KidsLiveTvFilter _filter;
    private readonly Guid _peke;
    private readonly Guid _news;
    private readonly Guid _movie = Guid.NewGuid();

    public KidsLiveTvFilterTests()
    {
        _gate.Kids.Add(Kid);
        _peke = _items.AddChannel();
        _news = _items.AddChannel();
        _gate.Allowed[Kid] = [_peke];
        _filter = new KidsLiveTvFilter(_gate, _items, _streams, new FakeTimeProvider(), NullLogger<KidsLiveTvFilter>.Instance);
    }

    [Fact]
    public async Task Kid_PlaybackInfoForAnotherChannel_Gets200NotAllowed_WithoutRunningTheAction()
    {
        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Negotiate, Args(("itemId", _news)));

        Assert.False(run.Ran);
        var answer = Assert.IsType<PlaybackInfoResponse>(Assert.IsType<OkObjectResult>(run.Context.Result).Value);
        Assert.Equal(PlaybackErrorCode.NotAllowed, answer.ErrorCode);
        Assert.Equal("liveTvChannel", run.Context.HttpContext.Response.Headers[PlaybackRefusal.ReasonHeader]);
        Assert.False(run.Context.HttpContext.Response.Headers.ContainsKey(PlaybackRefusal.ErrorCodeHeader));
    }

    [Fact]
    public async Task Kid_AllowedChannel_Runs_AndRecordsTheOpenedLiveStream()
    {
        _items.OpenStreams["live-peke"] = "unique-peke";
        var response = new PlaybackInfoResponse { MediaSources = [new MediaSourceInfo { LiveStreamId = "live-peke" }] };

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Negotiate, Args(("itemId", _peke)), new OkObjectResult(response));

        Assert.True(run.Ran);
        Assert.Null(run.Context.Result);
        Assert.True(_streams.TryGetChannel("live-peke", out var byId));
        Assert.Equal(_peke, byId);
        Assert.True(_streams.TryGetChannel("unique-peke", out var byUnique));
        Assert.Equal(_peke, byUnique);
    }

    [Fact]
    public async Task Kid_OpenWithAnotherChannelsTokenInTheBody_Gets403()
    {
        var body = new OpenStandIn { OpenToken = LiveTvIds.ProviderPrefix + "LiveTvChannel_" + _news.ToString("N") + "_src" };

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Open, Args(("openLiveStreamDto", body)));

        Assert.False(run.Ran);
        AssertForbidden(run.Context, "liveTvChannel");
    }

    [Fact]
    public async Task Kid_BodyNamingAnotherChannelOrAnUnknownLiveStream_Gets403()
    {
        var item = new OpenStandIn { ItemId = _news };
        var stream = new OpenStandIn { LiveStreamId = LiveTvIds.ProviderPrefix + "someone-elses" };

        var byItem = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Open, Args(("openLiveStreamDto", item)));
        var byStream = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Open, Args(("openLiveStreamDto", stream)));

        Assert.False(byItem.Ran);
        AssertForbidden(byItem.Context, "liveTvChannel");
        Assert.False(byStream.Ran);
        AssertForbidden(byStream.Context, "liveTvChannel");
    }

    [Fact]
    public async Task Kid_OpenWithTheirChannelsToken_RecordsTheLiveStreamFromTheResponse()
    {
        var body = new OpenStandIn { OpenToken = LiveTvIds.ProviderPrefix + "LiveTvChannel_" + _peke.ToString("N") + "_src" };
        var response = new LiveStreamResponse(new MediaSourceInfo { LiveStreamId = LiveTvIds.ProviderPrefix + "peke" });

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Open, Args(("openLiveStreamDto", body)), new OkObjectResult(response));

        Assert.True(run.Ran);
        Assert.True(_streams.TryGetChannel(LiveTvIds.ProviderPrefix + "peke", out var channel));
        Assert.Equal(_peke, channel);
    }

    [Fact]
    public async Task Kid_HlsOfTheirChannel_WithAStreamTheyOpened_Runs()
    {
        _streams.Record(LiveTvIds.ProviderPrefix + "peke", null, _peke, _ => true);

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("itemId", _peke), ("liveStreamId", LiveTvIds.ProviderPrefix + "peke")));

        Assert.True(run.Ran);
    }

    [Fact]
    public async Task Kid_HlsOfTheirChannel_WithAnotherChannelsStream_Gets403()
    {
        _streams.Record(LiveTvIds.ProviderPrefix + "news", null, _news, _ => true);

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("itemId", _peke), ("liveStreamId", LiveTvIds.ProviderPrefix + "news")));

        Assert.False(run.Ran);
        AssertForbidden(run.Context, "liveTvChannel");
    }

    [Fact]
    public async Task Kid_UnknownLiveTvStream_Gets403_ButOtherProvidersStreamsPass()
    {
        var unknown = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("itemId", _movie), ("liveStreamId", LiveTvIds.ProviderPrefix + "someone-elses")));
        var other = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("itemId", _movie), ("liveStreamId", "0123456789abcdef0123456789abcdef_x")));

        Assert.False(unknown.Ran);
        AssertForbidden(unknown.Context, "liveTvChannel");
        Assert.True(other.Ran);
    }

    [Fact]
    public async Task Kid_LiveStreamFile_IsJudgedByTheStreamItOpened()
    {
        _streams.Record("live-peke", "unique-peke", _peke, _ => true);
        _streams.Record("live-news", "unique-news", _news, _ => true);

        var own = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("streamId", "unique-peke")), controller: "LiveTv");
        var other = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("streamId", "unique-news")), controller: "LiveTv");
        var unknown = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("streamId", "unique-x")), controller: "LiveTv");

        Assert.True(own.Ran);
        Assert.False(other.Ran);
        Assert.False(unknown.Ran);
        AssertForbidden(unknown.Context, "liveTvChannel");
    }

    [Fact]
    public async Task Kid_StreamIdOutsideTheLiveTvController_IsNotALiveStreamFile()
    {
        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("itemId", _movie), ("streamId", "unique-x")));

        Assert.True(run.Ran);
        Assert.Null(run.Context.Result);
    }

    [Fact]
    public async Task Kid_GuideProgram_IsJudgedByItsChannel()
    {
        var newsProgram = _items.AddProgram(_news);
        var pekeProgram = _items.AddProgram(_peke);

        var refused = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Negotiate, Args(("itemId", newsProgram)));
        var allowed = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Negotiate, Args(("itemId", pekeProgram)));

        Assert.False(refused.Ran);
        Assert.True(allowed.Ran);
    }

    [Fact]
    public async Task Kid_LibraryPlayback_IsNotAffected_EvenWithoutChannels()
    {
        _gate.Allowed.Remove(Kid);

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("itemId", _movie), ("liveStreamId", null)));

        Assert.True(run.Ran);
    }

    [Fact]
    public async Task KidWithoutChannels_IsRefusedEveryChannel()
    {
        _gate.Allowed.Remove(Kid);

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Deliver, Args(("itemId", _peke.ToString("N"))));

        Assert.False(run.Ran);
        AssertForbidden(run.Context, "liveTvChannel");
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("User")]
    public async Task AdminsAndAdults_AreNeverFiltered(string role)
    {
        var user = role == "Administrator" ? User(Kid, role) : User(Adult);

        var playback = await RunAsync(user, LiveTvRule.Playback, GateKind.Negotiate, Args(("itemId", _news)));
        var recording = await RunAsync(user, LiveTvRule.Recording, GateKind.Deliver, Args());

        Assert.True(playback.Ran);
        Assert.True(recording.Ran);
    }

    [Fact]
    public async Task TokenlessRequest_FromAKidsDevice_IsJudged_OthersPass()
    {
        _gate.Devices["tv-1"] = Kid;
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var kids = await RunAsync(anonymous, LiveTvRule.Playback, GateKind.Deliver, Args(("itemId", _news)), query: "?deviceId=tv-1");
        var server = await RunAsync(anonymous, LiveTvRule.Playback, GateKind.Deliver, Args(("streamId", "unique-x")), controller: "LiveTv");

        Assert.False(kids.Ran);
        Assert.True(server.Ran);
    }

    [Fact]
    public async Task Kid_Recordings_Get403LiveTvRecording()
    {
        var run = await RunAsync(User(Kid), LiveTvRule.Recording, GateKind.Deliver, Args());

        Assert.False(run.Ran);
        AssertForbidden(run.Context, "liveTvRecording");
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<JsonResult>(run.Context.Result).Value);
        Assert.Equal("Recordings are not available.", problem.Title);
    }

    [Fact]
    public async Task Kid_ChannelList_KeepsTheirChannels_PagedAndCountedAfterwards()
    {
        var classics = _items.AddChannel();
        _gate.Allowed[Kid] = [_peke, classics];
        var all = new QueryResult<BaseItemDto>(null, 3, [Dto(_news), Dto(_peke), Dto(classics)]);
        var arguments = Args(("startIndex", 1), ("limit", 1));

        var run = await RunAsync(User(Kid), LiveTvRule.ChannelList, GateKind.Deliver, arguments, new OkObjectResult(all));

        Assert.True(run.Ran);
        Assert.Null(arguments["startIndex"]);
        Assert.Null(arguments["limit"]);
        var page = Assert.IsType<QueryResult<BaseItemDto>>(run.Executed!.Result is ObjectResult o ? o.Value : null);
        Assert.Equal(2, page.TotalRecordCount);
        Assert.Equal(1, page.StartIndex);
        Assert.Equal([classics], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Kid_RecommendedPrograms_KeepOnlyTheirChannels()
    {
        var all = new QueryResult<BaseItemDto>(null, 3, [Program(_news), Program(_peke), new BaseItemDto { Id = Guid.NewGuid() }]);

        var run = await RunAsync(User(Kid), LiveTvRule.RecommendedPrograms, GateKind.Deliver, Args(), new OkObjectResult(all));

        var kept = Assert.IsType<QueryResult<BaseItemDto>>(((ObjectResult)run.Executed!.Result!).Value);
        Assert.Equal([_peke], kept.Items.Select(i => i.ChannelId));
        Assert.Equal(1, kept.TotalRecordCount);
    }

    [Fact]
    public async Task Kid_ProgramList_IsNarrowedToTheirChannels()
    {
        var none = Args(("channelIds", Array.Empty<Guid>()));
        var asked = Args(("channelIds", new[] { _news, _peke }));

        await RunAsync(User(Kid), LiveTvRule.ProgramList, GateKind.Deliver, none);
        await RunAsync(User(Kid), LiveTvRule.ProgramList, GateKind.Deliver, asked);

        Assert.Equal([_peke], (Guid[])none["channelIds"]!);
        Assert.Equal([_peke], (Guid[])asked["channelIds"]!);
    }

    [Fact]
    public async Task Kid_ProgramList_WithNothingLeft_IsEmptyWithoutRunningTheAction()
    {
        var run = await RunAsync(User(Kid), LiveTvRule.ProgramList, GateKind.Deliver, Args(("channelIds", new[] { _news })));

        Assert.False(run.Ran);
        var empty = Assert.IsType<QueryResult<BaseItemDto>>(Assert.IsType<OkObjectResult>(run.Context.Result).Value);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task Kid_PostedProgramList_HasItsBodyNarrowed()
    {
        var body = new ProgramsStandIn { ChannelIds = [_news, _peke] };

        var run = await RunAsync(User(Kid), LiveTvRule.ProgramListPosted, GateKind.Deliver, Args(("body", body)));

        Assert.True(run.Ran);
        Assert.Equal([_peke], body.ChannelIds);
    }

    [Fact]
    public async Task Kid_SingleChannelOrProgramOfAnotherChannel_Is404()
    {
        var newsProgram = _items.AddProgram(_news);

        var channel = await RunAsync(User(Kid), LiveTvRule.Channel, GateKind.Deliver, Args(("channelId", _news)));
        var program = await RunAsync(User(Kid), LiveTvRule.Program, GateKind.Deliver, Args(("programId", newsProgram.ToString("N"))));
        var own = await RunAsync(User(Kid), LiveTvRule.Channel, GateKind.Deliver, Args(("channelId", _peke)));
        var notLiveTv = await RunAsync(User(Kid), LiveTvRule.Channel, GateKind.Deliver, Args(("channelId", _movie)));

        Assert.IsType<NotFoundResult>(channel.Context.Result);
        Assert.IsType<NotFoundResult>(program.Context.Result);
        Assert.True(own.Ran);
        Assert.True(notLiveTv.Ran);
    }

    [Theory]
    [InlineData("X_{0}_src")]
    [InlineData("LiveTvChannel_{1}_src")]
    [InlineData("LiveTvChannel_not-a-guid_src")]
    public async Task Kid_CraftedOrUnreadableLiveTvToken_IsRefused(string shape)
    {
        // Jellyfin opens the second part of the token as the channel, in any GUID format, whatever the first part says
        var token = LiveTvIds.ProviderPrefix + string.Format(System.Globalization.CultureInfo.InvariantCulture, shape, _news.ToString("N"), _news.ToString("D"));

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Open, Args(("openToken", token)));

        Assert.False(run.Ran);
        AssertForbidden(run.Context, "liveTvChannel");
    }

    [Fact]
    public async Task Kid_LiveTvTokenNamingSomethingElseThanAChannel_IsRefused()
    {
        var token = LiveTvIds.ProviderPrefix + "LiveTvChannel_" + _movie.ToString("N") + "_src";

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Open, Args(("openToken", token)));

        Assert.False(run.Ran);
    }

    [Fact]
    public async Task Kid_OtherProvidersOpenToken_Runs()
    {
        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Open, Args(("openToken", "0123456789abcdef0123456789abcdef_channel-item")));

        Assert.True(run.Ran);
    }

    [Fact]
    public async Task Kid_PostedProgramList_OnlyForbiddenChannels_IsEmptyWithoutRunningTheAction()
    {
        var body = new ProgramsStandIn { ChannelIds = [_news] };

        var run = await RunAsync(User(Kid), LiveTvRule.ProgramListPosted, GateKind.Deliver, Args(("body", body)));

        Assert.False(run.Ran);
        Assert.Empty(Assert.IsType<QueryResult<BaseItemDto>>(Assert.IsType<OkObjectResult>(run.Context.Result).Value).Items);
        Assert.Equal([_news], body.ChannelIds);
    }

    [Fact]
    public async Task Kid_PostedProgramList_WithoutChannels_GetsTheirs()
    {
        var body = new ProgramsStandIn { ChannelIds = [] };

        var run = await RunAsync(User(Kid), LiveTvRule.ProgramListPosted, GateKind.Deliver, Args(("body", body)));

        Assert.True(run.Ran);
        Assert.Equal([_peke], body.ChannelIds);
    }

    [Fact]
    public async Task Kid_OwnProgram_Runs()
    {
        var run = await RunAsync(User(Kid), LiveTvRule.Program, GateKind.Deliver, Args(("programId", _items.AddProgram(_peke).ToString("N"))));

        Assert.True(run.Ran);
        Assert.Null(run.Context.Result);
    }

    [Fact]
    public async Task Kid_RecommendedPrograms_ArePagedAfterFiltering()
    {
        var pekePrograms = new[] { Program(_peke), Program(_peke), Program(_peke) };
        var all = new QueryResult<BaseItemDto>(null, 4, [Program(_news), .. pekePrograms]);
        var arguments = Args(("startIndex", 1), ("limit", 1));

        var run = await RunAsync(User(Kid), LiveTvRule.RecommendedPrograms, GateKind.Deliver, arguments, new OkObjectResult(all));

        Assert.True(run.Ran);
        Assert.Null(arguments["startIndex"]);
        Assert.Null(arguments["limit"]);
        var page = Assert.IsType<QueryResult<BaseItemDto>>(((ObjectResult)run.Executed!.Result!).Value);
        Assert.Equal(3, page.TotalRecordCount);
        Assert.Equal(1, page.StartIndex);
        Assert.Equal([pekePrograms[1].Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task FailureAfterTheAction_LeavesItsAnswer()
    {
        _items.Throw = true;
        _items.OpenStreams["live-peke"] = "unique-peke";
        var response = new OkObjectResult(new PlaybackInfoResponse { MediaSources = [new MediaSourceInfo { LiveStreamId = "live-peke" }] });

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Negotiate, Args(("itemId", _peke)), response);

        Assert.True(run.Ran);
        Assert.Same(response, run.Executed!.Result);
        Assert.False(_streams.TryGetChannel("live-peke", out _));
    }

    [Fact]
    public async Task FilterFailure_LetsTheRequestThrough()
    {
        _gate.Throw = true;

        var run = await RunAsync(User(Kid), LiveTvRule.Playback, GateKind.Negotiate, Args(("itemId", _news)));

        Assert.True(run.Ran);
        Assert.Null(run.Context.Result);
    }

    private static void AssertForbidden(ActionExecutingContext context, string reason)
    {
        var result = Assert.IsType<JsonResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("application/problem+json", result.ContentType);
        Assert.Equal(reason, context.HttpContext.Response.Headers[PlaybackRefusal.ReasonHeader]);
        Assert.Equal("PlaybackNotAllowed", context.HttpContext.Response.Headers[PlaybackRefusal.ErrorCodeHeader]);
        Assert.Equal(reason, Assert.IsType<ProblemDetails>(result.Value).Extensions["reason"]);
    }

    private static BaseItemDto Dto(Guid id) => new() { Id = id };

    private static BaseItemDto Program(Guid channel) => new() { Id = Guid.NewGuid(), ChannelId = channel };

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] arguments)
        => arguments.ToDictionary(a => a.Name, a => a.Value);

    private static ClaimsPrincipal User(Guid userId, string role = "User")
        => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role), new Claim("Jellyfin-UserId", userId.ToString("N"))],
            "Custom"));

    private async Task<Run> RunAsync(
        ClaimsPrincipal user,
        LiveTvRule rule,
        GateKind kind,
        Dictionary<string, object?> arguments,
        IActionResult? result = null,
        string controller = "MediaInfo",
        string query = "")
    {
        var http = new DefaultHttpContext { User = user };
        http.Request.QueryString = new QueryString(query);
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = "Action" };
        descriptor.Properties[typeof(LiveTvRule)] = rule;
        descriptor.Properties[typeof(GateKind)] = kind;
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), descriptor), [], arguments, new object());
        var ran = false;
        ActionExecutedContext? executed = null;
        await _filter.OnActionExecutionAsync(context, () =>
        {
            ran = true;
            executed = new ActionExecutedContext(context, [], new object()) { Result = result };
            return Task.FromResult(executed);
        });
        return new Run(context, ran, executed);
    }

    private sealed record Run(ActionExecutingContext Context, bool Ran, ActionExecutedContext? Executed);

    // Stand-ins for Jellyfin.Api's OpenLiveStreamDto and GetProgramsDto, read by property name
    private sealed class OpenStandIn
    {
        public string? OpenToken { get; set; }

        public Guid? ItemId { get; set; }

        public string? LiveStreamId { get; set; }
    }

    private sealed class ProgramsStandIn
    {
        public IReadOnlyList<Guid>? ChannelIds { get; set; }
    }
}
