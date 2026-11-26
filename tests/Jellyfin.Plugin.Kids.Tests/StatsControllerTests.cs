using System;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Kids.Api;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.ParentLock;
using Jellyfin.Plugin.Kids.Stats;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class StatsControllerTests : IDisposable
{
    private const string Token = "token";
    private const string Device = "tv-1";

    private readonly StatsFixture _f = new();
    private readonly FakeAuthorizationContext _auth = new();
    private readonly FakeDeviceDirectory _devices = new();
    private readonly GrantService _grants;
    private readonly StatsController _controller;

    public StatsControllerTests()
    {
        _grants = new GrantService(_f.Time);
        _devices.DevicesByToken[Token] = Device;
        _controller = new StatsController(
            new CallerResolver(_auth, _devices, _grants),
            _f.Service,
            _f.Watch,
            _f.Store,
            _f.Settings,
            NullLogger<StatsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public void Dispose() => _f.Dispose();

    private static User NewUser(string name, bool admin = false)
    {
        var user = new User(name, "auth", "reset");
        user.SetPermission(PermissionKind.IsAdministrator, admin);
        return user;
    }

    private void SignIn(User user, string? grant = null)
    {
        _auth.Info = new AuthorizationInfo { User = user, Token = Token, DeviceId = Device, IsAuthenticated = true };
        var http = new DefaultHttpContext();
        if (grant is not null)
        {
            http.Request.Headers[CallerResolver.GrantHeader] = grant;
        }

        _controller.ControllerContext = new ControllerContext { HttpContext = http };
    }

    [Fact]
    public async Task Admin_GetsTheStats()
    {
        var kid = await _f.MarkAsync(60);
        SignIn(NewUser("admin", admin: true));

        var result = await _controller.GetStats(kid, null, TestContext.Current.CancellationToken);

        Assert.Equal(7, result.Value!.Days.Count);
    }

    [Fact]
    public async Task GrantHolder_GetsTheStats()
    {
        var kidUser = NewUser("manuel");
        var kid = await _f.MarkAsync(60, kidUser.Id);
        var (grant, _) = _grants.Issue(kid, "device:" + Device);
        SignIn(kidUser, grant);

        var result = await _controller.GetStats(kid, 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Value!.Days.Count);
    }

    [Fact]
    public async Task KidWithoutGrant_IsRefused_EvenForThemselves()
    {
        var kidUser = NewUser("manuel");
        var kid = await _f.MarkAsync(60, kidUser.Id);
        SignIn(kidUser);

        Assert.IsType<ForbidResult>((await _controller.GetStats(kid, null, TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task OtherKid_IsRefused()
    {
        var kid = await _f.MarkAsync(60);
        var other = NewUser("lucia");
        await _f.MarkAsync(60, other.Id);
        SignIn(other);

        Assert.IsType<ForbidResult>((await _controller.GetStats(kid, null, TestContext.Current.CancellationToken)).Result);
        Assert.IsType<ForbidResult>((await _controller.GetStatsDetail(kid, null, TestContext.Current.CancellationToken)).Result);
        Assert.IsType<ForbidResult>(await _controller.DeleteWatchLog(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GrantHolder_CannotDeleteTheWatchLog()
    {
        var kidUser = NewUser("manuel");
        var kid = await _f.MarkAsync(60, kidUser.Id);
        var (grant, _) = _grants.Issue(kid, "device:" + Device);
        SignIn(kidUser, grant);

        Assert.IsType<ForbidResult>(await _controller.DeleteWatchLog(TestContext.Current.CancellationToken));
        Assert.IsType<ForbidResult>((await _controller.GetWatchLog(TestContext.Current.CancellationToken)).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task DaysOutOfRange_Is400(int days)
    {
        var kid = await _f.MarkAsync(60);
        SignIn(NewUser("admin", admin: true));

        var result = await _controller.GetStats(kid, days, TestContext.Current.CancellationToken);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Days", ((ValidationProblemDetails)bad.Value!).Errors.Keys);
    }

    [Fact]
    public async Task NotAKid_Is404()
    {
        SignIn(NewUser("admin", admin: true));

        Assert.IsType<NotFoundObjectResult>((await _controller.GetStats(Guid.NewGuid(), null, TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task Shape_MatchesTheContract()
    {
        var kid = await _f.MarkAsync(60, Guid.Parse("5b2a4e8f-0c6d-4d4e-9f1a-1c2d3e4f5a6b"));
        _f.Settings.AdvancedStats = true;
        var series = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
        var at = new DateTimeOffset(2026, 11, 19, 17, 0, 0, TimeSpan.Zero);
        _f.Play(kid, new WatchItem(Guid.NewGuid(), "Episode", "Taxi", series, "Bluey", 2, 5), at, 42 * 60);
        await _f.UsageStore.AddBonusAsync(kid, "2026-11-19", 15, "admin", at);
        _f.Watch.OnBreak(new Enforcement.BreakNotice(kid, at.AddMinutes(30), TimeSpan.FromMinutes(15), false));
        SignIn(NewUser("admin", admin: true));

        var stats = (await _controller.GetStats(kid, 2, TestContext.Current.CancellationToken)).Value!;
        var json = JsonSerializer.Serialize(stats, JsonDefaults.PascalCaseOptions);
        TestContext.Current.SendDiagnosticMessage("Stats sample: {0}", json);

        Assert.Equal(
            string.Concat(
                """{"UserId":"5b2a4e8f0c6d4d4e9f1a1c2d3e4f5a6b","Today":"2026-11-19","AdvancedStats":true,"RetentionDays":90,""",
                """ "Days":[{"Day":"2026-11-18","UsedMinutes":0,"BonusMinutes":0,"AllowanceMinutes":60},{"Day":"2026-11-19","UsedMinutes":42,"BonusMinutes":15,"AllowanceMinutes":60}],""".TrimStart(),
                """ "TopSeries":[{"SeriesId":"0f1e2d3c4b5a69788796a5b4c3d2e1f0","Name":"Bluey","Minutes":42,"Plays":1,"IsLiveTv":false}],""".TrimStart(),
                """ "Breaks":[{"Day":"2026-11-19","Count":1,"Minutes":15}]}""".TrimStart()),
            json);
    }

    [Fact]
    public async Task Shape_WritesNullAllowanceAndEmptyListsWhenOff()
    {
        var kid = await _f.MarkAsync(null);
        SignIn(NewUser("admin", admin: true));

        var stats = (await _controller.GetStats(kid, 1, TestContext.Current.CancellationToken)).Value!;
        var json = JsonSerializer.Serialize(stats, JsonDefaults.PascalCaseOptions);

        Assert.Contains("\"AllowanceMinutes\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"AdvancedStats\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"TopSeries\":[],\"Breaks\":[]", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_DeletesTheWatchLog()
    {
        var kid = await _f.MarkAsync(60);
        _f.Settings.AdvancedStats = true;
        _f.Play(kid, new WatchItem(Guid.NewGuid(), "Movie", "Coco"), _f.Time.Now.AddHours(-1), 60);
        await _f.Watch.FlushAsync(TestContext.Current.CancellationToken);
        SignIn(NewUser("admin", admin: true));

        Assert.Equal(1, (await _controller.GetWatchLog(TestContext.Current.CancellationToken)).Value!.Segments);
        Assert.IsType<NoContentResult>(await _controller.DeleteWatchLog(TestContext.Current.CancellationToken));
        var info = (await _controller.GetWatchLog(TestContext.Current.CancellationToken)).Value!;
        Assert.Equal(new WatchLogInfo(true, 90, 0, 0, null), info);
    }

    private sealed class FakeAuthorizationContext : IAuthorizationContext
    {
        public AuthorizationInfo Info { get; set; } = new();

        public Task<AuthorizationInfo> GetAuthorizationInfo(HttpContext requestContext) => Task.FromResult(Info);

        public Task<AuthorizationInfo> GetAuthorizationInfo(HttpRequest requestContext) => Task.FromResult(Info);
    }
}
