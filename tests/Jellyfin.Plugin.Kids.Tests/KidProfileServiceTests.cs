using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class KidProfileServiceTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly KidsDatabase _db;
    private readonly FakeUserDirectory _users = new();
    private readonly FakePickCatalog _catalog = new();
    private readonly KidRoster _roster = new();
    private readonly KidProfileService _service;
    private readonly Guid _alice = Guid.NewGuid();

    public KidProfileServiceTests()
    {
        _db = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        _service = new KidProfileService(new KidProfileStore(_db), _users, _catalog, new FakeLiveTvChannelCatalog(), _roster);
        _users.Users.Add(_alice);
    }

    public void Dispose()
    {
        _db.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task Mark_ExistingUser_CreatesDefaultsAtRevisionOne()
    {
        var result = await _service.SaveAsync(_alice, 0, new KidProfileInput());

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Equal(new KidProfile(_alice, 1, new KidProfileSettings()), result.Profile, ProfileComparer.Instance);
    }

    [Fact]
    public async Task Mark_UnknownUser_IsNotFound()
    {
        var result = await _service.SaveAsync(Guid.NewGuid(), 0, new KidProfileInput());

        Assert.Equal(SaveOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Mark_ExistingKid_IsConflictWithCurrentAndChangesNothing()
    {
        await _service.SaveAsync(_alice, 0, new KidProfileInput { AutoplayCap = 7 });

        var result = await _service.SaveAsync(_alice, 0, new KidProfileInput { SearchAllowed = true });

        Assert.Equal(SaveOutcome.Conflict, result.Outcome);
        Assert.Equal(7, result.Profile!.Settings.AutoplayCap);
        Assert.False((await _service.GetAsync(_alice))!.Settings.SearchAllowed);
    }

    [Fact]
    public async Task Update_CurrentRevision_Increments()
    {
        await _service.SaveAsync(_alice, 0, new KidProfileInput());

        var result = await _service.SaveAsync(_alice, 1, new KidProfileInput { AutoplayEnabled = true, AutoplayCap = 2 });

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Equal(2, result.Profile!.Revision);
        Assert.True(result.Profile.Settings.AutoplayEnabled);
    }

    [Fact]
    public async Task Update_StaleRevision_IsConflictWithCurrent()
    {
        await _service.SaveAsync(_alice, 0, new KidProfileInput());
        await _service.SaveAsync(_alice, 1, new KidProfileInput { SearchAllowed = true });

        var result = await _service.SaveAsync(_alice, 1, new KidProfileInput { AutoplayCap = 9 });

        Assert.Equal(SaveOutcome.Conflict, result.Outcome);
        Assert.Equal(2, result.Profile!.Revision);
        Assert.True(result.Profile.Settings.SearchAllowed);
    }

    [Fact]
    public async Task Update_NotAKid_IsNotFound()
    {
        var result = await _service.SaveAsync(_alice, 1, new KidProfileInput());

        Assert.Equal(SaveOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Save_NegativeRevision_IsInvalid()
    {
        var result = await _service.SaveAsync(_alice, -1, new KidProfileInput());

        Assert.Equal(SaveOutcome.Invalid, result.Outcome);
        Assert.Contains("Revision", result.Errors!.Keys);
    }

    [Fact]
    public async Task Save_InvalidSettings_IsInvalidAndStoresNothing()
    {
        var result = await _service.SaveAsync(_alice, 0, new KidProfileInput { AutoplayCap = 11 });

        Assert.Equal(SaveOutcome.Invalid, result.Outcome);
        Assert.Contains("AutoplayCap", result.Errors!.Keys);
        Assert.Null(await _service.GetAsync(_alice));
    }

    [Fact]
    public async Task Update_KeepsStoredPickDeletedFromLibrary()
    {
        var pick = _catalog.Add("Bluey");
        await _service.SaveAsync(_alice, 0, new KidProfileInput { PickedItemIds = [pick.Id] });
        _catalog.Items.Clear();

        var result = await _service.SaveAsync(_alice, 1, new KidProfileInput { PickedItemIds = [pick.Id], SearchAllowed = true });

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Equal([pick.Id], result.Profile!.Settings.PickedItemIds);
    }

    [Fact]
    public async Task Unmark_RemovesThenNotFound()
    {
        await _service.SaveAsync(_alice, 0, new KidProfileInput());

        Assert.True(await _service.DeleteAsync(_alice));
        Assert.False(await _service.DeleteAsync(_alice));
        Assert.Null(await _service.GetAsync(_alice));
    }

    [Fact]
    public async Task Mark_AddsToTheRoster_UnmarkRemoves()
    {
        await _service.SaveAsync(_alice, 0, new KidProfileInput());
        Assert.True(_roster.Contains(_alice));

        await _service.DeleteAsync(_alice);
        Assert.False(_roster.Contains(_alice));
    }

    [Fact]
    public async Task FailedMark_DoesNotAddToTheRoster()
    {
        await _service.SaveAsync(_alice, 0, new KidProfileInput { AutoplayCap = 11 });
        await _service.SaveAsync(Guid.NewGuid(), 0, new KidProfileInput());

        Assert.False(_roster.Contains(_alice));
    }

    [Fact]
    public async Task LoadRoster_ReadsEveryKid()
    {
        var bob = Guid.NewGuid();
        _users.Users.Add(bob);
        await _service.SaveAsync(_alice, 0, new KidProfileInput());
        await _service.SaveAsync(bob, 0, new KidProfileInput());
        var roster = new KidRoster();

        await new KidProfileService(new KidProfileStore(_db), _users, _catalog, new FakeLiveTvChannelCatalog(), roster).LoadRosterAsync(TestContext.Current.CancellationToken);

        Assert.True(roster.IsLoaded);
        Assert.True(roster.Contains(_alice));
        Assert.True(roster.Contains(bob));
    }

    [Fact]
    public async Task Save_WithoutTheAllowance_ClearsIt()
    {
        await _service.SaveAsync(_alice, 0, new KidProfileInput { DailyAllowanceMinutes = 45 });

        var result = await _service.SaveAsync(_alice, 1, new KidProfileInput { SearchAllowed = true });

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Null(result.Profile!.Settings.DailyAllowanceMinutes);
        Assert.Null((await _service.GetAsync(_alice))!.Settings.DailyAllowanceMinutes);
    }

    [Fact]
    public async Task Save_WithoutTheSessionLimit_ClearsIt()
    {
        var saved = await _service.SaveAsync(_alice, 0, new KidProfileInput { SessionLimitMinutes = 30 });
        Assert.Equal(30, saved.Profile!.Settings.SessionLimitMinutes);

        var result = await _service.SaveAsync(_alice, 1, new KidProfileInput { SearchAllowed = true });

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Null(result.Profile!.Settings.SessionLimitMinutes);
        Assert.Null((await _service.GetAsync(_alice))!.Settings.SessionLimitMinutes);
    }

    [Fact]
    public async Task Save_SessionLimitOutOfRange_IsInvalidAndKeepsTheProfile()
    {
        await _service.SaveAsync(_alice, 0, new KidProfileInput { SessionLimitMinutes = 30 });

        var result = await _service.SaveAsync(_alice, 1, new KidProfileInput { SessionLimitMinutes = 121 });

        Assert.Equal(SaveOutcome.Invalid, result.Outcome);
        Assert.Contains("SessionLimitMinutes", result.Errors!.Keys);
        var stored = (await _service.GetAsync(_alice))!;
        Assert.Equal(1, stored.Revision);
        Assert.Equal(30, stored.Settings.SessionLimitMinutes);
    }

    [Fact]
    public async Task Get_NotAKid_IsNull()
    {
        Assert.Null(await _service.GetAsync(_alice));
    }
}
