using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class KidsPlaybackFilterTests
{
    private static readonly Guid Kid = Guid.NewGuid();
    private static readonly Guid Adult = Guid.NewGuid();
    private static readonly Denial TimeUp = new(DenialReason.TimeUp, new DateTimeOffset(2026, 10, 19, 22, 0, 0, TimeSpan.Zero));

    private readonly FakePlaybackGate _gate = new();
    private readonly DeliveryRegistry _deliveries = new();
    private readonly KidsPlaybackFilter _filter;

    public KidsPlaybackFilterTests()
    {
        _gate.Kids.Add(Kid);
        _filter = new KidsPlaybackFilter(_gate, _deliveries, new FakeTimeProvider(), NullLogger<KidsPlaybackFilter>.Instance);
    }

    [Fact]
    public async Task DeniedKid_PlaybackInfo_Gets200NotAllowed()
    {
        _gate.Denied[Kid] = TimeUp;
        var (context, ran) = await RunAsync(User(Kid), GateKind.Negotiate);

        Assert.False(ran);
        var ok = Assert.IsType<OkObjectResult>(context.Result);
        var response = Assert.IsType<PlaybackInfoResponse>(ok.Value);
        Assert.Equal(PlaybackErrorCode.NotAllowed, response.ErrorCode);
        Assert.Empty(response.MediaSources);
        Assert.Equal("timeUp", context.HttpContext.Response.Headers[PlaybackRefusal.ReasonHeader]);
        Assert.False(context.HttpContext.Response.Headers.ContainsKey(PlaybackRefusal.ErrorCodeHeader));
    }

    [Theory]
    [InlineData(GateKind.Deliver)]
    [InlineData(GateKind.Open)]
    public async Task DeniedKid_OtherActions_Get403PlaybackNotAllowed(GateKind kind)
    {
        _gate.Denied[Kid] = TimeUp;
        var (context, ran) = await RunAsync(User(Kid), kind);

        Assert.False(ran);
        var result = Assert.IsType<JsonResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("application/problem+json", result.ContentType);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("timeUp", problem.Extensions["reason"]);
        Assert.Equal("2026-10-19T22:00:00.0000000Z", problem.Extensions["until"]);
        Assert.Equal("PlaybackNotAllowed", context.HttpContext.Response.Headers[PlaybackRefusal.ErrorCodeHeader]);
        Assert.Equal("timeUp", context.HttpContext.Response.Headers[PlaybackRefusal.ReasonHeader]);
    }

    [Fact]
    public async Task Administrator_IsNeverRefused()
    {
        _gate.Kids.Add(Adult);
        _gate.Denied[Adult] = TimeUp;
        var (context, ran) = await RunAsync(User(Adult, "Administrator"), GateKind.Deliver);

        Assert.True(ran);
        Assert.Null(context.Result);
        Assert.Equal(0, _deliveries.Count);
    }

    [Fact]
    public async Task EnforcementOff_LetsDeniedKidThrough()
    {
        _gate.IsEnabled = false;
        _gate.Denied[Kid] = TimeUp;
        var (context, ran) = await RunAsync(User(Kid), GateKind.Negotiate);

        Assert.True(ran);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task NotAKid_PassesUntracked()
    {
        var tracked = -1;
        var (_, ran) = await RunAsync(User(Adult), GateKind.Deliver, () => tracked = _deliveries.Count);

        Assert.True(ran);
        Assert.Equal(0, tracked);
    }

    [Fact]
    public async Task AllowedKid_DeliveryIsTrackedWhileItStreams()
    {
        var tracked = -1;
        var (_, ran) = await RunAsync(User(Kid), GateKind.Deliver, () => tracked = _deliveries.Count);

        Assert.True(ran);
        Assert.Equal(1, tracked);
        Assert.Equal(0, _deliveries.Count);
    }

    [Fact]
    public async Task AllowedKid_PlaybackInfoIsNotTracked()
    {
        var tracked = -1;
        await RunAsync(User(Kid), GateKind.Negotiate, () => tracked = _deliveries.Count);

        Assert.Equal(0, tracked);
    }

    [Fact]
    public async Task Tokenless_KidDevice_IsRefused()
    {
        _gate.Devices["tv-1"] = Kid;
        _gate.Denied[Kid] = TimeUp;
        var (context, ran) = await RunAsync(new ClaimsPrincipal(new ClaimsIdentity()), GateKind.Deliver, query: "?static=true&deviceId=tv-1");

        Assert.False(ran);
        Assert.Equal(403, Assert.IsType<JsonResult>(context.Result).StatusCode);
    }

    [Fact]
    public async Task Tokenless_UnknownDevice_IsServed()
    {
        _gate.Denied[Kid] = TimeUp;
        var (_, ran) = await RunAsync(new ClaimsPrincipal(new ClaimsIdentity()), GateKind.Deliver, query: "?deviceId=laptop");

        Assert.True(ran);
    }

    [Fact]
    public async Task GateFailure_FailsOpen()
    {
        _gate.Throw = true;
        _gate.Denied[Kid] = TimeUp;
        var (context, ran) = await RunAsync(User(Kid), GateKind.Deliver);

        Assert.True(ran);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task ActionFailure_IsNotSwallowed()
    {
        var context = Context(User(Kid), GateKind.Deliver, string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _filter.OnResourceExecutionAsync(context, () => throw new InvalidOperationException("action")));
        Assert.Equal(0, _deliveries.Count);
    }

    private static ClaimsPrincipal User(Guid userId, string role = "User")
        => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role), new Claim("Jellyfin-UserId", userId.ToString("N"))],
            "Custom"));

    private static ResourceExecutingContext Context(ClaimsPrincipal user, GateKind kind, string query)
    {
        var http = new DefaultHttpContext { User = user };
        http.Request.QueryString = new QueryString(query);
        http.Features.Set<IHttpRequestLifetimeFeature>(new DeliveryRegistryTests.FakeLifetime());
        var descriptor = new ActionDescriptor { Properties = new Dictionary<object, object?> { [typeof(GateKind)] = kind } };
        return new ResourceExecutingContext(new ActionContext(http, new RouteData(), descriptor), [], []);
    }

    private async Task<(ResourceExecutingContext Context, bool Ran)> RunAsync(ClaimsPrincipal user, GateKind kind, Action? during = null, string query = "")
    {
        var context = Context(user, kind, query);
        var ran = false;
        await _filter.OnResourceExecutionAsync(context, () =>
        {
            ran = true;
            during?.Invoke();
            return Task.FromResult(new ResourceExecutedContext(context, []));
        });
        return (context, ran);
    }
}
