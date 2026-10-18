using System;
using Jellyfin.Plugin.Kids.Auth;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class ProfileAccessTests
{
    private static readonly Guid _kid = Guid.NewGuid();

    private static readonly CallerContext _self = new(_kid, IsAdmin: false, IsApiKey: false);
    private static readonly CallerContext _other = new(Guid.NewGuid(), IsAdmin: false, IsApiKey: false);
    private static readonly CallerContext _admin = new(Guid.NewGuid(), IsAdmin: true, IsApiKey: false);
    private static readonly CallerContext _apiKey = new(Guid.Empty, IsAdmin: true, IsApiKey: true);

    [Fact]
    public void CanRead_SelfAdminAndApiKey()
    {
        Assert.True(ProfileAccess.CanRead(_self, _kid));
        Assert.True(ProfileAccess.CanRead(_admin, _kid));
        Assert.True(ProfileAccess.CanRead(_apiKey, _kid));
    }

    [Fact]
    public void CanRead_OtherUser_IsRefused()
    {
        Assert.False(ProfileAccess.CanRead(_other, _kid));
    }

    [Fact]
    public void CanRead_AnonymousCallerNeverMatchesEmptyUserId()
    {
        Assert.False(ProfileAccess.CanRead(new CallerContext(Guid.Empty, IsAdmin: false, IsApiKey: false), Guid.Empty));
    }

    [Fact]
    public void CanMutate_OnlyAdminAndApiKey()
    {
        Assert.True(ProfileAccess.CanMutate(_admin));
        Assert.True(ProfileAccess.CanMutate(_apiKey));
        Assert.False(ProfileAccess.CanMutate(_self));
        Assert.False(ProfileAccess.CanMutate(_other));
    }
}
