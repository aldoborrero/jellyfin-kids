using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class BreakNoticeTests
{
    private static readonly Guid Kid = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 19);

    private readonly KidRoster _roster = new();
    private readonly FakeKidsSettings _settings = new();
    private readonly EnforcementState _state;
    private readonly List<BreakNotice> _notices = [];

    public BreakNoticeTests()
    {
        _roster.Replace([Kid]);
        _state = new EnforcementState(_roster, _settings, new FakePlaybackControl());
        _state.BreakChanged += (_, e) => _notices.Add(e.Argument);
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), 30, null);
    }

    [Fact]
    public void ReachingTheSittingLimit_StartsOneBreak()
    {
        _state.OnPlayed(Kid, Now, Now.AddMinutes(20));
        Assert.Empty(_notices);

        _state.OnPlayed(Kid, Now.AddMinutes(20), Now.AddMinutes(30));
        _state.OnPlayed(Kid, Now.AddMinutes(30), Now.AddMinutes(31));

        Assert.Equal([new BreakNotice(Kid, Now.AddMinutes(30), TimeSpan.FromMinutes(15), false)], _notices);
    }

    [Fact]
    public void EndBreak_NotesWhenAndHowLong()
    {
        _state.OnPlayed(Kid, Now, Now.AddMinutes(30));

        _state.EndBreak(Kid, Now.AddMinutes(35));

        Assert.Equal(new BreakNotice(Kid, Now.AddMinutes(30), TimeSpan.FromMinutes(5), true), _notices[^1]);
    }

    [Fact]
    public void Reset_InABreak_NotesIt_OutsideABreak_Nothing()
    {
        _state.ResetSitting(Kid, Now);
        Assert.Empty(_notices);

        _state.OnPlayed(Kid, Now, Now.AddMinutes(30));
        _state.ResetSitting(Kid, Now.AddMinutes(32));

        Assert.Equal(new BreakNotice(Kid, Now.AddMinutes(30), TimeSpan.FromMinutes(2), true), _notices[^1]);
    }

    [Fact]
    public void PlayCountedAfterTheLimit_DoesNotMoveTheBreak_SoEndingItNotesTheSameBreak()
    {
        // The limit is reached at 30 min, but reports still count a few seconds before playback is stopped (or for
        // longer with enforcement off): the break the grown-up ends is the one that started at 30 min
        _state.OnPlayed(Kid, Now, Now.AddMinutes(30));
        _state.OnPlayed(Kid, Now.AddMinutes(30), Now.AddMinutes(30).AddSeconds(10));

        _state.EndBreak(Kid, Now.AddMinutes(35));

        Assert.Equal(
            [
                new BreakNotice(Kid, Now.AddMinutes(30), TimeSpan.FromMinutes(15), false),
                new BreakNotice(Kid, Now.AddMinutes(30), TimeSpan.FromMinutes(5), true),
            ],
            _notices);
    }

    [Fact]
    public void Reset_AfterPlayCountedPastTheLimit_NotesTheSameBreak()
    {
        _state.OnPlayed(Kid, Now, Now.AddMinutes(30));
        _state.OnPlayed(Kid, Now.AddMinutes(30), Now.AddMinutes(31));

        _state.ResetSitting(Kid, Now.AddMinutes(33));

        Assert.Equal(new BreakNotice(Kid, Now.AddMinutes(30), TimeSpan.FromMinutes(3), true), _notices[^1]);
    }

    [Fact]
    public void ANewSittingAfterABreak_NotesItsOwnBreak()
    {
        _state.OnPlayed(Kid, Now, Now.AddMinutes(30));
        var later = Now.AddHours(2);
        _state.OnPlayed(Kid, later, later.AddMinutes(30));

        _state.EndBreak(Kid, later.AddMinutes(31));

        Assert.Equal(new BreakNotice(Kid, later.AddMinutes(30), TimeSpan.FromMinutes(1), true), _notices[^1]);
    }

    [Fact]
    public void NoSittingLimit_NoBreaks()
    {
        _state.SetKid(Kid, new KidBudget(Today, 0, null, 0), null, null);

        _state.OnPlayed(Kid, Now, Now.AddHours(2));

        Assert.Empty(_notices);
    }
}
