using System;
using System.Collections.Generic;
using Jellyfin.Data;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class EnforcementStateTests
{
    private static readonly Guid Kid = Guid.NewGuid();

    // 2026-10-19 16:00 UTC; with UTC days starting at midnight, today is 2026-10-19
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 19);

    private readonly KidRoster _roster = new();
    private readonly FakeKidsSettings _settings = new() { EnforcementEnabled = true };
    private readonly FakePlaybackControl _control = new();
    private readonly EnforcementState _state;
    private readonly List<Guid> _reloads = [];

    public EnforcementStateTests()
    {
        _roster.Replace([Kid]);
        _state = new EnforcementState(_roster, _settings, _control);
        _state.ReloadRequested += (_, e) => _reloads.Add(e.Argument);
    }

    [Fact]
    public void NotAKid_IsNeverDenied()
    {
        var other = Guid.NewGuid();
        _state.SetKid(other, new KidBudget(Today, 0, 0, 0), null, null);

        Assert.Null(_state.Evaluate(other, Now));
        Assert.False(_state.TryGetDenial(other, Now, out _));
    }

    [Fact]
    public void AllowanceUsedUp_IsTimeUpUntilTheNextDay()
    {
        _state.SetKid(Kid, new KidBudget(Today, 29 * 60_000, 30, 0), null, null);
        Assert.Null(_state.Evaluate(Kid, Now));

        _state.OnPlayed(Kid, Now, Now.AddMinutes(1));

        var denial = _state.Evaluate(Kid, Now.AddMinutes(1));
        Assert.Equal(new Denial(DenialReason.TimeUp, new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero)), denial);
        Assert.Equal("timeUp", denial!.Code);
    }

    [Fact]
    public void Disabled_TryGetDenialIsFalse_ButEvaluateStillKnows()
    {
        _settings.EnforcementEnabled = false;
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);

        Assert.False(_state.TryGetDenial(Kid, Now, out _));
        Assert.Equal(DenialReason.TimeUp, _state.Evaluate(Kid, Now)!.Reason);
        Assert.False(_state.Describe(Kid, Now).Enforced);
        Assert.Null(_state.Describe(Kid, Now).Denial);
    }

    [Fact]
    public void NewDay_LiftsTimeUpAndAsksForAReload()
    {
        _state.SetKid(Kid, new KidBudget(Today, 30 * 60_000, 30, 0), null, null);
        Assert.Equal(DenialReason.TimeUp, _state.Evaluate(Kid, Now)!.Reason);

        Assert.Null(_state.Evaluate(Kid, Now.AddHours(9)));
        Assert.Equal([Kid], _reloads);
    }

    [Fact]
    public void NotLoaded_IsNotDeniedAndAsksForALoadAtMostEvery30Seconds()
    {
        Assert.Null(_state.Evaluate(Kid, Now));
        Assert.Null(_state.Evaluate(Kid, Now.AddSeconds(10)));
        Assert.Single(_reloads);

        _state.Evaluate(Kid, Now.AddSeconds(31));
        Assert.Equal(2, _reloads.Count);
    }

    [Fact]
    public void OutsideAllowedHours_IsDeniedFirst()
    {
        var user = new User("alice", "auth", "reset");

        // An empty window (0–0 h) never allows a moment with a non-zero time of day
        user.AccessSchedules.Add(new AccessSchedule(DynamicDayOfWeek.Everyday, 0, 0, user.Id));
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, user);

        Assert.Equal(new Denial(DenialReason.OutsideSchedule, null), _state.Evaluate(Kid, Now));
    }

    [Fact]
    public void WithinAllowedHours_IsNotDenied()
    {
        var user = new User("alice", "auth", "reset");
        user.AccessSchedules.Add(new AccessSchedule(DynamicDayOfWeek.Everyday, 0, 24, user.Id));
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), null, user);

        Assert.Null(_state.Evaluate(Kid, Now));
    }

    [Fact]
    public void UserUpdate_AppliesAtOnce()
    {
        var user = new User("alice", "auth", "reset");
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), null, user);
        Assert.Null(_state.Evaluate(Kid, Now));

        var updated = new User("alice", "auth", "reset");
        updated.AccessSchedules.Add(new AccessSchedule(DynamicDayOfWeek.Everyday, 0, 0, updated.Id));
        _state.SetUser(Kid, updated);

        Assert.Equal(DenialReason.OutsideSchedule, _state.Evaluate(Kid, Now)!.Reason);
    }

    [Fact]
    public void SittingLimit_DeniesUntilTheBreakIsOver_AcrossDevices()
    {
        _settings.SittingBreak = TimeSpan.FromMinutes(15);
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), 30, null);

        // Two devices, one after the other, 5 minutes apart: one sitting
        _state.OnPlayed(Kid, Now, Now.AddMinutes(20));
        _state.OnPlayed(Kid, Now.AddMinutes(25), Now.AddMinutes(35));

        var denial = _state.Evaluate(Kid, Now.AddMinutes(35));
        Assert.Equal(new Denial(DenialReason.SessionLimit, Now.AddMinutes(50)), denial);
        Assert.Null(_state.Evaluate(Kid, Now.AddMinutes(50)));
    }

    [Fact]
    public void Describe_ReportsTheSitting()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), 30, null);
        _state.OnPlayed(Kid, Now, Now.AddMinutes(10));

        var view = _state.Describe(Kid, Now.AddMinutes(10));
        Assert.True(view.Enforced);
        Assert.Null(view.Denial);
        Assert.Equal(TimeSpan.FromMinutes(30), view.SittingLimit);
        Assert.Equal(10 * 60_000, view.Sitting!.UsedMs);
        Assert.Equal(20 * 60_000, view.Sitting.RemainingMs);
        Assert.Equal(TimeSpan.FromMinutes(15), view.Break);
    }

    [Fact]
    public void Forget_DropsTheKid()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, 0, 0), null, null);
        _state.Forget(Kid);

        Assert.Empty(_state.LoadedKids());
    }

    [Fact]
    public void EndingSoon_ListsTheAllowanceAndTheSitting()
    {
        _state.SetKid(Kid, new KidBudget(Today, (30 * 60_000) - 200_000 - (9 * 60_000), 30, 0), 10, null);
        _state.OnPlayed(Kid, Now.AddMinutes(-9), Now);

        var ending = _state.EndingSoon(Kid, Now);

        Assert.Contains(ending, e => e.Limit == DenialReason.TimeUp && e.RemainingMs == 200_000 && e.Instance == "2026-10-19");
        Assert.Contains(ending, e => e.Limit == DenialReason.SessionLimit && e.RemainingMs == 60_000);
    }

    [Fact]
    public void TimeUpAndSittingLimitTogether_ReportTimeUp()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, 30, 0), 30, null);
        _state.OnPlayed(Kid, Now, Now.AddMinutes(30));

        Assert.Equal(DenialReason.TimeUp, _state.Evaluate(Kid, Now.AddMinutes(30))!.Reason);
    }

    [Fact]
    public void EndingSoon_ListsTheAllowedHoursClosing()
    {
        // Allowed until 16:03 UTC (the fake zone is UTC); it is 16:00
        var user = new User("alice", "auth", "reset");
        user.AccessSchedules.Add(new AccessSchedule(DynamicDayOfWeek.Everyday, 0, 16.05, user.Id));
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), null, user);

        var ending = Assert.Single(_state.EndingSoon(Kid, Now));

        Assert.Equal(DenialReason.OutsideSchedule, ending.Limit);
        Assert.InRange(ending.RemainingMs, 180_000, 181_000);
        Assert.Equal("2026-10-19T16:03", ending.Instance);
    }

    [Fact]
    public void EndingSoon_NoSchedule_ListsNothing()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), null, new User("alice", "auth", "reset"));

        Assert.Empty(_state.EndingSoon(Kid, Now));
    }

    [Fact]
    public void KidForDevice_FindsTheKidsSession()
    {
        _control.Current.Add(new KidSession("s1", Kid, "alice", "web", "tv-1", true, false, null));
        _control.Current.Add(new KidSession("s2", Guid.NewGuid(), "admin", "web", "laptop", true, false, null));

        Assert.Equal(Kid, _state.KidForDevice("tv-1"));
        Assert.Equal(Guid.Empty, _state.KidForDevice("laptop"));
        Assert.Equal(Guid.Empty, _state.KidForDevice("nope"));
    }

    [Fact]
    public void LiveTv_AllowedChannelsAreTheProfileList_WhenLiveTvAccessIsOn()
    {
        var peke = Guid.NewGuid();
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), null, LiveTvUser(true), [peke]);

        Assert.Equal([peke], _state.AllowedChannels(Kid));
    }

    [Fact]
    public void LiveTv_AccessOff_AllowsNothing_AndAUserUpdateAppliesAtOnce()
    {
        var peke = Guid.NewGuid();
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), null, LiveTvUser(false), [peke]);
        Assert.Empty(_state.AllowedChannels(Kid));

        _state.SetUser(Kid, LiveTvUser(true));

        Assert.Equal([peke], _state.AllowedChannels(Kid));
    }

    [Fact]
    public void LiveTv_NoListOrNoUser_AllowsNothing()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), null, LiveTvUser(true));
        Assert.Empty(_state.AllowedChannels(Kid));

        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), null, null, [Guid.NewGuid()]);
        Assert.Empty(_state.AllowedChannels(Kid));
    }

    [Fact]
    public void LiveTv_KidNotLoadedYet_AllowsNothingAndAsksForALoad()
    {
        Assert.Empty(_state.AllowedChannels(Kid));
        Assert.Equal([Kid], _reloads);
    }

    [Fact]
    public void LiveTv_NotAKid_AllowsNothingWithoutALoad()
    {
        Assert.Empty(_state.AllowedChannels(Guid.NewGuid()));
        Assert.Empty(_reloads);
    }

    private static User LiveTvUser(bool liveTvAccess)
    {
        var user = new User("alice", "auth", "reset");
        user.SetPermission(PermissionKind.EnableLiveTvAccess, liveTvAccess);
        return user;
    }

    [Fact]
    public void ResetSitting_LiftsTheSittingLimitAtOnce()
    {
        _settings.SittingBreak = TimeSpan.FromMinutes(15);
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), 30, null);
        _state.OnPlayed(Kid, Now, Now.AddMinutes(30));
        Assert.Equal(DenialReason.SessionLimit, _state.Evaluate(Kid, Now.AddMinutes(30))!.Reason);

        _state.ResetSitting(Kid);

        Assert.Null(_state.Evaluate(Kid, Now.AddMinutes(30)));
        Assert.Equal(0, _state.Describe(Kid, Now.AddMinutes(30)).Sitting!.UsedMs);
    }

    [Fact]
    public void ResetSitting_OfAnUnknownKid_DoesNothing()
    {
        _state.ResetSitting(Guid.NewGuid());
        Assert.Empty(_state.LoadedKids());
    }
}
