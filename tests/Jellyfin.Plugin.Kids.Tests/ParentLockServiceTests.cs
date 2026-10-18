using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.ParentLock;
using Jellyfin.Plugin.Kids.Storage;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public sealed class ParentLockServiceTests : IDisposable
{
    private static readonly CallerContext _admin = new(Guid.NewGuid(), IsAdmin: true, IsApiKey: false, UserName: "admin");
    private static readonly CallerContext _kid = new(Guid.NewGuid(), IsAdmin: false, IsApiKey: false, DeviceKey: "device:A", UserName: "alice");

    private readonly TempFolder _folder = new();
    private readonly KidsDatabase _database;
    private readonly ParentLockStore _store;
    private readonly FakeTimeProvider _time = new();
    private readonly GrantService _grants;
    private readonly ThrottleService _throttle;
    private readonly ParentLockService _service;

    public ParentLockServiceTests()
    {
        _database = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        _store = new ParentLockStore(_database);
        _grants = new GrantService(_time);
        _throttle = new ThrottleService(_time, NullLogger<ThrottleService>.Instance);
        _service = new ParentLockService(_store, _grants, _throttle, _time, NullLogger<ParentLockService>.Instance);
    }

    public void Dispose()
    {
        _database.Dispose();
        _folder.Dispose();
    }

    // Admin path (US3)

    [Fact]
    public async Task Set_WhenEmpty_StoresAVerifierNotTheCode()
    {
        var result = await _service.SetAsync(_admin, new LockUpdateInput("numeric", "2468", null, null));

        Assert.Equal(LockChangeOutcome.Saved, result.Outcome);
        Assert.Equal(ParentLockType.Numeric, result.Lock!.LockType);
        Assert.Equal(_time.Now.UtcDateTime, result.Lock.ChangedAt);
        var stored = await _store.GetAsync();
        Assert.True(Verifier.Verify("2468", stored!.Verifier));
        Assert.DoesNotContain("2468", stored.Verifier, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_AsAdmin_ReplacesWithoutCurrent()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");

        var result = await _service.SetAsync(_admin, new LockUpdateInput("REMOTE", "UDLRrg", null, null));

        Assert.Equal(LockChangeOutcome.Saved, result.Outcome);
        Assert.True(Verifier.Verify("UDLRrg", (await _store.GetAsync())!.Verifier));
    }

    [Theory]
    [InlineData("NUMERIC", "123", null, "Code")]
    [InlineData("NUMERIC", "12a4", null, "Code")]
    [InlineData("REMOTE", "UDLX", null, "Code")]
    [InlineData("REMOTE", "UDLR", "pbkdf2-sha256$1$00$00", "Code")]
    [InlineData("REMOTE", null, null, "Code")]
    [InlineData("CHALLENGE", "1234", null, "LockType")]
    [InlineData(null, "1234", null, "LockType")]
    public async Task Set_Invalid_NamesTheFieldAndStoresNothing(string? lockType, string? code, string? verifier, string field)
    {
        var result = await _service.SetAsync(_admin, new LockUpdateInput(lockType, code, verifier, null));

        Assert.Equal(LockChangeOutcome.Invalid, result.Outcome);
        Assert.True(result.Errors!.ContainsKey(field));
        Assert.Null(await _store.GetAsync());
    }

    [Fact]
    public async Task Set_ByKidWithoutGrant_IsForbidden()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");

        var result = await _service.SetAsync(_kid, new LockUpdateInput("NUMERIC", "1357", null, "2468"));

        Assert.Equal(LockChangeOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Status_WhenEmpty_IsNull()
    {
        Assert.Null(await _service.GetAsync());
    }

    [Fact]
    public async Task Remove_AdminOnly()
    {
        Assert.Equal(LockChangeOutcome.NoLock, (await _service.RemoveAsync(_admin)).Outcome);
        await SetAsAdmin(ParentLockType.Numeric, "2468");

        Assert.Equal(LockChangeOutcome.Forbidden, (await _service.RemoveAsync(_kid with { HasGrant = true })).Outcome);
        Assert.Equal(LockChangeOutcome.Saved, (await _service.RemoveAsync(_admin)).Outcome);
        Assert.Null(await _service.GetAsync());
    }

    // Verify (US1)

    [Fact]
    public async Task Verify_RightCode_IssuesAGrantForThisUserAndDevice()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");

        var outcome = await _service.VerifyAsync(_kid, "2468");

        Assert.True(outcome.Valid);
        Assert.Equal(_time.Now.UtcDateTime.AddMinutes(5), outcome.ExpiresAt);
        Assert.True(_grants.IsValid(outcome.Grant, _kid.UserId, "device:A"));
        Assert.False(_grants.IsValid(outcome.Grant, _kid.UserId, "device:B"));
    }

    [Fact]
    public async Task Verify_WrongCode_IsNotValid()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");

        var outcome = await _service.VerifyAsync(_kid, "2469");

        Assert.Equal(VerifyOutcome.NotValid, outcome);
    }

    [Fact]
    public async Task Verify_NoLock_IsNotValidAndNotCounted()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(VerifyOutcome.NotValid, await _service.VerifyAsync(_kid, "2468"));
        }

        Assert.Equal(0, _throttle.Count);
    }

    [Fact]
    public async Task Verify_SixthAttempt_IsThrottledAndNotChecked()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");
        for (var i = 0; i < 5; i++)
        {
            Assert.Null((await _service.VerifyAsync(_kid, "0000")).RetryAfterSeconds);
        }

        var outcome = await _service.VerifyAsync(_kid, "2468");

        Assert.False(outcome.Valid);
        Assert.Equal(30, outcome.RetryAfterSeconds);
    }

    [Fact]
    public async Task Verify_TooLongOrNoDevice_IsNotValid()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");

        Assert.False((await _service.VerifyAsync(_kid, new string('1', ParentLockService.MaxSubmittedLength + 1))).Valid);
        Assert.False((await _service.VerifyAsync(_kid with { DeviceKey = null }, "2468")).Valid);
        Assert.Equal(0, _throttle.Count);
    }

    [Fact]
    public async Task Verify_StoreUnavailable_IsNotValid()
    {
        Directory.CreateDirectory(_folder.Path);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_folder.Path, KidsDatabase.FileName)};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99";
            command.ExecuteNonQuery();
        }

        Assert.Equal(VerifyOutcome.NotValid, await _service.VerifyAsync(_kid, "2468"));
    }

    // Grant use (US2)

    [Fact]
    public async Task Set_WithGrantAndRightCurrent_Replaces_AndRevokesGrants()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");
        var caller = await KidWithGrant("2468");

        var result = await _service.SetAsync(caller.Caller, new LockUpdateInput("REMOTE", "UDLRrg", null, "2468"));

        Assert.Equal(LockChangeOutcome.Saved, result.Outcome);
        Assert.True(Verifier.Verify("UDLRrg", (await _store.GetAsync())!.Verifier));
        Assert.False(_grants.IsValid(caller.Grant, _kid.UserId, "device:A"));
    }

    [Fact]
    public async Task Set_WithGrantAndWrongCurrent_IsInvalidAndCounts()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");
        var caller = await KidWithGrant("2468");

        var result = await _service.SetAsync(caller.Caller, new LockUpdateInput("NUMERIC", "1357", null, "0000"));

        Assert.Equal(LockChangeOutcome.Invalid, result.Outcome);
        Assert.True(result.Errors!.ContainsKey("Current"));
        Assert.True(Verifier.Verify("2468", (await _store.GetAsync())!.Verifier));
        Assert.Equal(2, _throttle.Count);
    }

    [Fact]
    public async Task Set_WithGrantAndMissingCurrent_IsInvalid()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");
        var caller = await KidWithGrant("2468");

        var result = await _service.SetAsync(caller.Caller, new LockUpdateInput("NUMERIC", "1357", null, null));

        Assert.True(result.Errors!.ContainsKey("Current"));
    }

    [Fact]
    public async Task Set_WithGrant_WhileThrottled_IsThrottled()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");
        var caller = await KidWithGrant("2468");
        for (var i = 0; i < 5; i++)
        {
            await _service.SetAsync(caller.Caller, new LockUpdateInput("NUMERIC", "1357", null, "0000"));
        }

        var result = await _service.SetAsync(caller.Caller, new LockUpdateInput("NUMERIC", "1357", null, "2468"));

        Assert.Equal(LockChangeOutcome.Throttled, result.Outcome);
        Assert.Equal(30, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task Set_WithGrant_RightCurrent_ResetsTheThrottle()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");
        var caller = await KidWithGrant("2468");
        await _service.SetAsync(caller.Caller, new LockUpdateInput("NUMERIC", "1357", null, "0000"));

        await _service.SetAsync(caller.Caller, new LockUpdateInput("NUMERIC", "1357", null, "2468"));

        Assert.Equal(0, _throttle.Count);
    }

    [Fact]
    public async Task Import_ByGrantHolder_IsForbidden()
    {
        var result = await _service.SetAsync(_kid with { HasGrant = true }, new LockUpdateInput("NUMERIC", null, VerifierTests.WholphinDigits2468, null));

        Assert.Equal(LockChangeOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task AnyChange_RevokesEveryGrant()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");
        var first = await KidWithGrant("2468");
        await SetAsAdmin(ParentLockType.Numeric, "1357");
        Assert.False(_grants.IsValid(first.Grant, _kid.UserId, "device:A"));

        var second = await KidWithGrant("1357");
        await _service.RemoveAsync(_admin);
        Assert.False(_grants.IsValid(second.Grant, _kid.UserId, "device:A"));
    }

    [Fact]
    public async Task Set_WithGrant_WhenChangedMeanwhile_NeverOverwrites()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");
        var original = (await _store.GetAsync())!;
        await _store.UpsertAsync(original with { Verifier = Verifier.Create("9999") });

        // The verifier just checked no longer matches the stored one
        Assert.False(await _store.ReplaceIfVerifierMatchesAsync(original.Verifier, original with { Verifier = Verifier.Create("1357") }));
        Assert.True(Verifier.Verify("9999", (await _store.GetAsync())!.Verifier));
    }

    // Import (US4)

    [Fact]
    public async Task Import_WhenEmpty_TheWholphinCodeVerifies()
    {
        var result = await _service.SetAsync(_admin, new LockUpdateInput("REMOTE", null, VerifierTests.WholphinRemoteUdlrRg12, null));

        Assert.Equal(LockChangeOutcome.Saved, result.Outcome);
        Assert.True((await _service.VerifyAsync(_kid, "UDLRrg12")).Valid);
    }

    [Fact]
    public async Task Import_WhenSet_IsAConflictAndChangesNothing()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");

        var result = await _service.SetAsync(_admin, new LockUpdateInput("REMOTE", null, VerifierTests.WholphinRemoteUdlrRg12, null));

        Assert.Equal(LockChangeOutcome.Conflict, result.Outcome);
        Assert.Equal(ParentLockType.Numeric, result.Lock!.LockType);
        Assert.True(Verifier.Verify("2468", (await _store.GetAsync())!.Verifier));
    }

    [Fact]
    public async Task Import_Invalid_NamesVerifier()
    {
        var result = await _service.SetAsync(_admin, new LockUpdateInput("REMOTE", null, "pbkdf2-sha256$1$73616c74$55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc", null));

        Assert.True(result.Errors!.ContainsKey("Verifier"));
        Assert.Null(await _store.GetAsync());
    }

    [Fact]
    public async Task Import_Concurrently_OneWins()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Task.Run(() => _service.SetAsync(_admin, new LockUpdateInput("NUMERIC", null, VerifierTests.WholphinDigits2468, null)))));

        Assert.Single(results, r => r.Outcome == LockChangeOutcome.Saved);
        Assert.All(results.Where(r => r.Outcome != LockChangeOutcome.Saved), r => Assert.Equal(LockChangeOutcome.Conflict, r.Outcome));
    }

    [Fact]
    public async Task Import_ByKid_IsForbidden()
    {
        var result = await _service.SetAsync(_kid, new LockUpdateInput("NUMERIC", null, VerifierTests.WholphinDigits2468, null));

        Assert.Equal(LockChangeOutcome.Forbidden, result.Outcome);
    }

    // Offline verifier (US5)

    [Fact]
    public async Task GetVerifier_AdminOrGrant_NeverAKidAlone()
    {
        Assert.Equal(LockChangeOutcome.NoLock, (await _service.GetVerifierAsync(_admin)).Outcome);
        await SetAsAdmin(ParentLockType.Remote, "UDLRrg12");

        Assert.Equal(LockChangeOutcome.Forbidden, (await _service.GetVerifierAsync(_kid)).Outcome);
        var granted = await _service.GetVerifierAsync(_kid with { HasGrant = true });
        Assert.Equal(LockChangeOutcome.Saved, granted.Outcome);
        Assert.True(Verifier.Verify("UDLRrg12", granted.Lock!.Verifier));
        Assert.Equal(ParentLockType.Remote, granted.Lock.LockType);
    }

    [Fact]
    public async Task Remove_RevokesGrants_AndTheVerifierIsGone()
    {
        await SetAsAdmin(ParentLockType.Numeric, "2468");
        var caller = await KidWithGrant("2468");

        Assert.Equal(LockChangeOutcome.Forbidden, (await _service.RemoveAsync(caller.Caller)).Outcome);
        Assert.Equal(LockChangeOutcome.Saved, (await _service.RemoveAsync(_admin)).Outcome);

        Assert.False(_grants.IsValid(caller.Grant, _kid.UserId, "device:A"));
        Assert.Equal(LockChangeOutcome.NoLock, (await _service.GetVerifierAsync(_admin)).Outcome);
    }

    private async Task SetAsAdmin(string lockType, string code)
    {
        var result = await _service.SetAsync(_admin, new LockUpdateInput(lockType, code, null, null));
        Assert.Equal(LockChangeOutcome.Saved, result.Outcome);
    }

    private async Task<(CallerContext Caller, string Grant)> KidWithGrant(string code)
    {
        var outcome = await _service.VerifyAsync(_kid, code);
        Assert.True(outcome.Valid);
        return (_kid with { HasGrant = _grants.IsValid(outcome.Grant, _kid.UserId, "device:A") }, outcome.Grant!);
    }
}
