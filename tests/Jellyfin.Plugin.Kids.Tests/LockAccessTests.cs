using System;
using Jellyfin.Plugin.Kids.Auth;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class LockAccessTests
{
    private static readonly CallerContext _admin = new(Guid.NewGuid(), IsAdmin: true, IsApiKey: false);
    private static readonly CallerContext _apiKey = new(Guid.Empty, IsAdmin: true, IsApiKey: true, DeviceKey: "apikey:x");
    private static readonly CallerContext _kid = new(Guid.NewGuid(), IsAdmin: false, IsApiKey: false, DeviceKey: "device:a");
    private static readonly CallerContext _kidWithGrant = _kid with { HasGrant = true };
    private static readonly CallerContext _anonymous = new(Guid.Empty, IsAdmin: false, IsApiKey: false);

    [Fact]
    public void ReadStatus_AnyoneSignedIn()
    {
        Assert.True(LockAccess.CanReadStatus(_admin));
        Assert.True(LockAccess.CanReadStatus(_apiKey));
        Assert.True(LockAccess.CanReadStatus(_kid));
        Assert.False(LockAccess.CanReadStatus(_anonymous));
    }

    [Fact]
    public void SetWithoutCurrent_AdminAndApiKeyOnly()
    {
        Assert.True(LockAccess.CanSetWithoutCurrent(_admin));
        Assert.True(LockAccess.CanSetWithoutCurrent(_apiKey));
        Assert.False(LockAccess.CanSetWithoutCurrent(_kid));
        Assert.False(LockAccess.CanSetWithoutCurrent(_kidWithGrant));
    }

    [Fact]
    public void SetWithCurrent_GrantHolders()
    {
        Assert.True(LockAccess.CanSetWithCurrent(_kidWithGrant));
        Assert.False(LockAccess.CanSetWithCurrent(_kid));
    }

    [Fact]
    public void ImportAndRemove_AdminOnly_NeverAGrant()
    {
        Assert.True(LockAccess.CanImport(_admin));
        Assert.True(LockAccess.CanRemove(_apiKey));
        Assert.False(LockAccess.CanImport(_kidWithGrant));
        Assert.False(LockAccess.CanRemove(_kidWithGrant));
        Assert.False(LockAccess.CanRemove(_kid));
    }

    [Fact]
    public void ReadVerifier_AdminOrGrant_NeverAKidAlone()
    {
        Assert.True(LockAccess.CanReadVerifier(_admin));
        Assert.True(LockAccess.CanReadVerifier(_apiKey));
        Assert.True(LockAccess.CanReadVerifier(_kidWithGrant));
        Assert.False(LockAccess.CanReadVerifier(_kid));
    }
}
