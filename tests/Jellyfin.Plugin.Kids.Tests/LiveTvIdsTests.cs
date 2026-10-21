using System;
using System.Globalization;
using Jellyfin.Plugin.Kids.LiveTv;
using MediaBrowser.Common.Extensions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class LiveTvIdsTests
{
    private static readonly Guid Channel = Guid.Parse("7f3c5b0e-55d2-4d8f-9a7e-2b1c3d4e5f60");

    [Fact]
    public void ProviderPrefix_IsTheMd5OfLiveTvsProviderType()
    {
        // MediaSourceManager.SetKeyProperties: provider.GetType().FullName.GetMD5().ToString("N") + "_"
        var expected = "Jellyfin.LiveTv.LiveTvMediaSourceProvider".GetMD5().ToString("N", CultureInfo.InvariantCulture) + "_";

        Assert.Equal(expected, LiveTvIds.ProviderPrefix);
        Assert.Equal(33, LiveTvIds.ProviderPrefix.Length);
    }

    [Theory]
    [InlineData("LiveTvChannel_7f3c5b0e55d24d8f9a7e2b1c3d4e5f60_a1b2")]
    [InlineData("LiveTvChannel_7f3c5b0e55d24d8f9a7e2b1c3d4e5f60_")]
    [InlineData("LiveTvProgram_7f3c5b0e55d24d8f9a7e2b1c3d4e5f60_x")]
    [InlineData("Movie_7f3c5b0e55d24d8f9a7e2b1c3d4e5f60_x")]
    [InlineData("X_7f3c5b0e-55d2-4d8f-9a7e-2b1c3d4e5f60_x")]
    [InlineData("LiveTvChannel_{7f3c5b0e-55d2-4d8f-9a7e-2b1c3d4e5f60}")]
    public void LiveTvOpenToken_GivesTheItemJellyfinWouldOpen(string token)
    {
        // LiveTvMediaSourceProvider.OpenMediaSource opens the second part whatever the first says, in any GUID format
        Assert.True(LiveTvIds.IsLiveTvOpenToken(LiveTvIds.ProviderPrefix + token));
        Assert.Equal(Channel, LiveTvIds.ItemFromOpenToken(LiveTvIds.ProviderPrefix + token));
    }

    [Theory]
    [InlineData("LiveTvChannel_not-a-guid_x")]
    [InlineData("LiveTvChannel")]
    [InlineData("LiveTvChannel_00000000000000000000000000000000_x")]
    [InlineData("")]
    public void UnreadableLiveTvOpenToken_IsLiveTvButNamesNoItem(string token)
    {
        Assert.True(LiveTvIds.IsLiveTvOpenToken(LiveTvIds.ProviderPrefix + token));
        Assert.Null(LiveTvIds.ItemFromOpenToken(LiveTvIds.ProviderPrefix + token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LiveTvChannel_7f3c5b0e55d24d8f9a7e2b1c3d4e5f60_a1b2")]
    [InlineData("0123456789abcdef0123456789abcdef_LiveTvChannel_7f3c5b0e55d24d8f9a7e2b1c3d4e5f60_a1b2")]
    public void TokensOfOtherProviders_AreNotLiveTv(string? token)
    {
        Assert.False(LiveTvIds.IsLiveTvOpenToken(token));
        Assert.Null(LiveTvIds.ItemFromOpenToken(token));
    }

    [Fact]
    public void LiveStreamIds_AreLiveTvOnlyWithTheProviderPrefix()
    {
        Assert.True(LiveTvIds.IsLiveTvStreamId(LiveTvIds.ProviderPrefix + "abc"));
        Assert.True(LiveTvIds.IsLiveTvStreamId(LiveTvIds.ProviderPrefix.ToUpperInvariant() + "abc"));
        Assert.False(LiveTvIds.IsLiveTvStreamId("0123456789abcdef0123456789abcdef_abc"));
        Assert.False(LiveTvIds.IsLiveTvStreamId(null));
    }
}
