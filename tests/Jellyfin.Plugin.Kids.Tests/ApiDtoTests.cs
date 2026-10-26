using System;
using Jellyfin.Plugin.Kids.Api;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class ApiDtoTests
{
    [Fact]
    public void ProfileDto_CarriesTheAvatar()
    {
        var profile = new KidProfile(Guid.NewGuid(), 3, new KidProfileSettings { Avatar = "sara" });

        Assert.Equal("sara", KidProfileDto.From(profile).Avatar);
        Assert.Null(KidProfileDto.From(profile with { Settings = new KidProfileSettings() }).Avatar);
    }

    [Fact]
    public void ProfileUpdate_PassesTheAvatarToValidation()
    {
        Assert.Equal("Max", new KidProfileUpdate { Revision = 1, Avatar = "Max" }.ToInput().Avatar);
        Assert.Null(new KidProfileUpdate { Revision = 1 }.ToInput().Avatar);
    }

    [Fact]
    public void ChannelDto_HasTheRatingAndTheHiddenFlagOnlyWhenAskedForAUser()
    {
        var channel = new LiveTvChannelInfo(Guid.NewGuid(), "Grown-ups News", "2", null, false, "TV-14");

        var plain = LiveTvChannelDto.From(channel);
        Assert.Equal("TV-14", plain.Rating);
        Assert.Null(plain.HiddenFromUser);
        Assert.True(LiveTvChannelDto.From(channel, true).HiddenFromUser);
        Assert.Null(LiveTvChannelDto.From(channel with { Rating = null }).Rating);
    }

    [Fact]
    public void FakeCatalog_VisibleTo_LeavesOutWhatIsHiddenFromThatUser()
    {
        var catalog = new FakeLiveTvChannelCatalog();
        var peke = catalog.Add("PekeTV", "1");
        var news = catalog.Add("Grown-ups News", "2", "TV-14");
        var alice = Guid.NewGuid();
        catalog.HiddenFrom[alice] = [peke.Id];

        Assert.Equal([news.Id], catalog.VisibleTo(alice));
        Assert.Equal(2, catalog.VisibleTo(Guid.NewGuid()).Count);
    }
}
