using System;
using Jellyfin.Plugin.Kids.ParentLock;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class GrantServiceTests
{
    private const string DeviceA = "device:A";

    private static readonly Guid _kid = Guid.NewGuid();

    private readonly FakeTimeProvider _time = new();
    private readonly GrantService _grants;

    public GrantServiceTests()
    {
        _grants = new GrantService(_time);
    }

    [Fact]
    public void Issue_Is43Base64UrlChars_ExpiringIn5Minutes()
    {
        var (grant, expiresAt) = _grants.Issue(_kid, DeviceA);

        Assert.Matches("^[A-Za-z0-9_-]{43}$", grant);
        Assert.Equal(_time.Now.UtcDateTime.AddMinutes(5), expiresAt);
        Assert.NotEqual(grant, _grants.Issue(_kid, DeviceA).Grant);
    }

    [Fact]
    public void IsValid_SameUserAndDeviceOnly()
    {
        var (grant, _) = _grants.Issue(_kid, DeviceA);

        Assert.True(_grants.IsValid(grant, _kid, DeviceA));
        Assert.True(_grants.IsValid(grant, _kid, DeviceA));
        Assert.False(_grants.IsValid(grant, _kid, "device:B"));
        Assert.False(_grants.IsValid(grant, Guid.NewGuid(), DeviceA));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-grant")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void IsValid_UnknownOrMalformed_IsFalse(string? grant)
    {
        _grants.Issue(_kid, DeviceA);

        Assert.False(_grants.IsValid(grant, _kid, DeviceA));
    }

    [Fact]
    public void IsValid_ExpiresAfter5Minutes()
    {
        var (grant, _) = _grants.Issue(_kid, DeviceA);

        _time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        Assert.True(_grants.IsValid(grant, _kid, DeviceA));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(_grants.IsValid(grant, _kid, DeviceA));
    }

    [Fact]
    public void RevokeAll_EndsEveryGrant()
    {
        var (a, _) = _grants.Issue(_kid, DeviceA);
        var other = Guid.NewGuid();
        var (b, _) = _grants.Issue(other, "device:B");

        _grants.RevokeAll();

        Assert.False(_grants.IsValid(a, _kid, DeviceA));
        Assert.False(_grants.IsValid(b, other, "device:B"));
    }

    [Fact]
    public void RevokeUser_EndsOnlyThatUsersGrants()
    {
        var (a, _) = _grants.Issue(_kid, DeviceA);
        var other = Guid.NewGuid();
        var (b, _) = _grants.Issue(other, "device:B");

        _grants.RevokeUser(_kid);

        Assert.False(_grants.IsValid(a, _kid, DeviceA));
        Assert.True(_grants.IsValid(b, other, "device:B"));
    }

    [Fact]
    public void RawGrantValues_AreNeverStored()
    {
        var (grant, _) = _grants.Issue(_kid, DeviceA);

        Assert.Equal(1, _grants.Count);
        Assert.False(_grants.HoldsRawValue(grant));
    }
}
