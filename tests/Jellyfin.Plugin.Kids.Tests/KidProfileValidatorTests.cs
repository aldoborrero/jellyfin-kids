using System;
using System.Linq;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class KidProfileValidatorTests
{
    private readonly FakePickCatalog _catalog = new();
    private readonly FakeLiveTvChannelCatalog _channels = new();

    [Fact]
    public void EmptyInput_GivesDefaults()
    {
        var result = KidProfileValidator.Validate(new KidProfileInput(), null, _catalog, _channels);

        Assert.True(result.IsValid);
        var settings = result.Settings!;
        Assert.False(settings.SearchAllowed);
        Assert.False(settings.AutoplayEnabled);
        Assert.Equal(KidProfileSettings.DefaultAutoplayCap, settings.AutoplayCap);
        Assert.Equal(HomeSectionEntry.Default, settings.Sections);
        Assert.Empty(settings.PickedItemIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-3)]
    public void AutoplayCap_OutOfRange_IsRejected(int cap)
    {
        var result = KidProfileValidator.Validate(new KidProfileInput { AutoplayCap = cap }, null, _catalog, _channels);

        Assert.Contains("AutoplayCap", result.Errors.Keys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void AutoplayCap_AtBounds_IsAccepted(int cap)
    {
        var result = KidProfileValidator.Validate(new KidProfileInput { AutoplayCap = cap }, null, _catalog, _channels);

        Assert.True(result.IsValid);
        Assert.Equal(cap, result.Settings!.AutoplayCap);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(1440)]
    public void DailyAllowance_InRangeOrUnlimited_IsKept(int? minutes)
    {
        var result = KidProfileValidator.Validate(new KidProfileInput { DailyAllowanceMinutes = minutes }, null, _catalog, _channels);

        Assert.True(result.IsValid);
        Assert.Equal(minutes, result.Settings!.DailyAllowanceMinutes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void DailyAllowance_OutOfRange_IsRejected(int minutes)
    {
        var result = KidProfileValidator.Validate(new KidProfileInput { DailyAllowanceMinutes = minutes }, null, _catalog, _channels);

        Assert.Equal(["Must be between 0 and 1440."], result.Errors["DailyAllowanceMinutes"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(120)]
    public void SessionLimit_InRangeOrNone_IsKept(int? minutes)
    {
        var result = KidProfileValidator.Validate(new KidProfileInput { SessionLimitMinutes = minutes }, null, _catalog, _channels);

        Assert.True(result.IsValid);
        Assert.Equal(minutes, result.Settings!.SessionLimitMinutes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(121)]
    public void SessionLimit_OutOfRange_IsRejected(int minutes)
    {
        var result = KidProfileValidator.Validate(new KidProfileInput { SessionLimitMinutes = minutes }, null, _catalog, _channels);

        Assert.False(result.IsValid);
        Assert.Null(result.Settings);
        Assert.Equal(["Must be between 5 and 120."], result.Errors["SessionLimitMinutes"]);
    }

    [Fact]
    public void SessionLimit_IsReportedWithOtherFieldErrors()
    {
        var input = new KidProfileInput { AutoplayCap = 11, DailyAllowanceMinutes = 1441, SessionLimitMinutes = 200 };

        var result = KidProfileValidator.Validate(input, null, _catalog, _channels);

        Assert.Equal(["AutoplayCap", "DailyAllowanceMinutes", "SessionLimitMinutes"], result.Errors.Keys.Order());
    }

    [Fact]
    public void EmptyInput_HasNoSessionLimit()
    {
        Assert.Null(KidProfileValidator.Validate(new KidProfileInput(), null, _catalog, _channels).Settings!.SessionLimitMinutes);
    }

    [Fact]
    public void Sections_AreParsedCaseInsensitivelyAndNormalized()
    {
        var input = new KidProfileInput { Sections = [new("picked", true), new("NEW", false)] };

        var result = KidProfileValidator.Validate(input, null, _catalog, _channels);

        Assert.True(result.IsValid);
        Assert.Equal(
            [
                new HomeSectionEntry(HomeSection.Picked, true),
                new HomeSectionEntry(HomeSection.New, false),
                new HomeSectionEntry(HomeSection.KeepWatching, true),
                new HomeSectionEntry(HomeSection.Libraries, true),
                new HomeSectionEntry(HomeSection.Favourites, true)
            ],
            result.Settings!.Sections);
    }

    [Fact]
    public void Sections_VisibleDefaultsToTrue()
    {
        var result = KidProfileValidator.Validate(new KidProfileInput { Sections = [new("New", null)] }, null, _catalog, _channels);

        Assert.True(result.Settings!.Sections[0].Visible);
    }

    [Theory]
    [InlineData("Shorts")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("Picked, New")]
    public void Sections_UnknownName_IsRejectedByIndex(string? name)
    {
        var input = new KidProfileInput { Sections = [new("Picked", true), new(name, true)] };

        var result = KidProfileValidator.Validate(input, null, _catalog, _channels);

        Assert.Equal(["Sections[1]"], result.Errors.Keys);
    }

    [Fact]
    public void Sections_Duplicate_IsRejectedByIndex()
    {
        var input = new KidProfileInput { Sections = [new("New", true), new("Picked", true), new("new", false)] };

        var result = KidProfileValidator.Validate(input, null, _catalog, _channels);

        Assert.Equal(["Sections[2]"], result.Errors.Keys);
    }

    [Fact]
    public void Picks_ExistingCollectionsAndPlaylists_AreAcceptedInOrder()
    {
        var playlist = _catalog.Add("Songs", PickType.Playlist);
        var boxSet = _catalog.Add("Bluey");

        var result = KidProfileValidator.Validate(new KidProfileInput { PickedItemIds = [playlist.Id, boxSet.Id] }, null, _catalog, _channels);

        Assert.True(result.IsValid);
        Assert.Equal([playlist.Id, boxSet.Id], result.Settings!.PickedItemIds);
    }

    [Fact]
    public void Picks_Duplicate_IsRejectedByIndex()
    {
        var boxSet = _catalog.Add("Bluey");

        var result = KidProfileValidator.Validate(new KidProfileInput { PickedItemIds = [boxSet.Id, boxSet.Id] }, null, _catalog, _channels);

        Assert.Equal(["PickedItemIds[1]"], result.Errors.Keys);
    }

    [Fact]
    public void Picks_MoreThanMax_IsRejected()
    {
        var ids = Enumerable.Range(0, KidProfileSettings.MaxPicks + 1).Select(i => _catalog.Add($"Item {i}").Id).ToList();

        var result = KidProfileValidator.Validate(new KidProfileInput { PickedItemIds = ids }, null, _catalog, _channels);

        Assert.Equal(["PickedItemIds"], result.Errors.Keys);
    }

    [Fact]
    public void Picks_NewIdNotResolved_IsRejectedByIndex()
    {
        var boxSet = _catalog.Add("Bluey");

        var result = KidProfileValidator.Validate(new KidProfileInput { PickedItemIds = [boxSet.Id, Guid.NewGuid()] }, null, _catalog, _channels);

        var message = Assert.Single(result.Errors["PickedItemIds[1]"]);
        Assert.Equal("Not a collection or playlist.", message);
    }

    [Fact]
    public void Picks_StoredIdNoLongerResolved_IsKept()
    {
        var deleted = Guid.NewGuid();

        var result = KidProfileValidator.Validate(new KidProfileInput { PickedItemIds = [deleted] }, new KidProfileSettings { PickedItemIds = [deleted] }, _catalog, _channels);

        Assert.True(result.IsValid);
        Assert.Equal([deleted], result.Settings!.PickedItemIds);
    }

    [Fact]
    public void SeveralErrors_AreReportedTogether()
    {
        var input = new KidProfileInput
        {
            AutoplayCap = 99,
            Sections = [new("Shorts", true)],
            PickedItemIds = [Guid.NewGuid()]
        };

        var result = KidProfileValidator.Validate(input, null, _catalog, _channels);

        Assert.Null(result.Settings);
        Assert.Equal(["AutoplayCap", "PickedItemIds[0]", "Sections[0]"], result.Errors.Keys.Order());
    }

    [Fact]
    public void LiveTvChannels_DefaultToNone()
    {
        var result = KidProfileValidator.Validate(new KidProfileInput { LiveTvChannelIds = null }, null, _catalog, _channels);

        Assert.True(result.IsValid);
        Assert.Empty(result.Settings!.LiveTvChannelIds);
    }

    [Fact]
    public void LiveTvChannels_ExistingChannels_AreKeptInOrder()
    {
        var classics = _channels.Add("PekeTV Classics", "2");
        var peke = _channels.Add("PekeTV", "1");

        var result = KidProfileValidator.Validate(new KidProfileInput { LiveTvChannelIds = [classics.Id, peke.Id] }, null, _catalog, _channels);

        Assert.True(result.IsValid);
        Assert.Equal([classics.Id, peke.Id], result.Settings!.LiveTvChannelIds);
    }

    [Fact]
    public void LiveTvChannels_UnknownId_IsAnErrorAtItsPosition()
    {
        var peke = _channels.Add("PekeTV");

        var result = KidProfileValidator.Validate(new KidProfileInput { LiveTvChannelIds = [peke.Id, Guid.NewGuid()] }, null, _catalog, _channels);

        Assert.False(result.IsValid);
        Assert.Equal("Not a live TV channel.", Assert.Single(result.Errors["LiveTvChannelIds[1]"]));
        Assert.False(result.Errors.ContainsKey("LiveTvChannelIds[0]"));
    }

    [Fact]
    public void LiveTvChannels_Duplicate_IsAnError()
    {
        var peke = _channels.Add("PekeTV");

        var result = KidProfileValidator.Validate(new KidProfileInput { LiveTvChannelIds = [peke.Id, peke.Id] }, null, _catalog, _channels);

        Assert.Equal("Listed more than once.", Assert.Single(result.Errors["LiveTvChannelIds[1]"]));
    }

    [Fact]
    public void LiveTvChannels_StoredChannelRemovedFromServer_IsKept()
    {
        var removed = Guid.NewGuid();

        var result = KidProfileValidator.Validate(
            new KidProfileInput { LiveTvChannelIds = [removed] },
            new KidProfileSettings { LiveTvChannelIds = [removed] },
            _catalog,
            _channels);

        Assert.True(result.IsValid);
        Assert.Equal([removed], result.Settings!.LiveTvChannelIds);
    }

    [Fact]
    public void LiveTvChannels_StoredPickIsNotAStoredChannel()
    {
        var pick = Guid.NewGuid();

        var result = KidProfileValidator.Validate(
            new KidProfileInput { LiveTvChannelIds = [pick] },
            new KidProfileSettings { PickedItemIds = [pick] },
            _catalog,
            _channels);

        Assert.Equal("Not a live TV channel.", Assert.Single(result.Errors["LiveTvChannelIds[0]"]));
    }

    [Fact]
    public void LiveTvChannels_MoreThan200_IsOneError()
    {
        var ids = Enumerable.Range(0, KidProfileSettings.MaxLiveTvChannels + 1).Select(_ => Guid.NewGuid()).ToList();

        var result = KidProfileValidator.Validate(new KidProfileInput { LiveTvChannelIds = ids }, null, _catalog, _channels);

        Assert.Equal("At most 200 channels.", Assert.Single(result.Errors["LiveTvChannelIds"]));
        Assert.DoesNotContain(result.Errors.Keys, k => k.StartsWith("LiveTvChannelIds[", StringComparison.Ordinal));
    }
}
