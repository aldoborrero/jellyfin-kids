using System;
using Jellyfin.Plugin.Kids.ParentLock;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class ThrottleServiceTests
{
    private const string User = "kid";
    private const string Device = "device:tv";

    private readonly FakeTimeProvider _time = new();
    private readonly ThrottleService _throttle;

    public ThrottleServiceTests()
    {
        _throttle = new ThrottleService(_time, NullLogger<ThrottleService>.Instance);
    }

    [Fact]
    public void FiveWrongCodes_ThenTheSixthIsRefused()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.True(_throttle.TryBegin(User, Device, out _));
            _throttle.RecordFailure(User, Device, "kid");
        }

        Assert.False(_throttle.TryBegin(User, Device, out var retry));
        Assert.Equal(30, retry);

        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(_throttle.TryBegin(User, Device, out retry));
        Assert.Equal(1, retry);
    }

    [Fact]
    public void EachWrongCodeAfterAWait_DoublesIt_UpTo15Minutes()
    {
        Fail(5);
        int[] expected = [60, 120, 240, 480, 900, 900];
        foreach (var wait in expected)
        {
            _time.Advance(TimeSpan.FromMinutes(16));
            Assert.True(_throttle.TryBegin(User, Device, out _));
            _throttle.RecordFailure(User, Device, "kid");
            Assert.False(_throttle.TryBegin(User, Device, out var retry));
            Assert.Equal(wait, retry);
        }
    }

    [Fact]
    public void RefusedAttempts_AreNotCounted()
    {
        Fail(5);
        for (var i = 0; i < 10; i++)
        {
            Assert.False(_throttle.TryBegin(User, Device, out _));
        }

        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(_throttle.TryBegin(User, Device, out _));
        _throttle.RecordFailure(User, Device, "kid");
        Assert.False(_throttle.TryBegin(User, Device, out var retry));
        Assert.Equal(60, retry);
    }

    [Fact]
    public void RightCode_ClearsBothCounts()
    {
        Fail(4);
        _throttle.RecordSuccess(User, Device);
        Fail(4);

        Assert.True(_throttle.TryBegin(User, Device, out _));
    }

    [Fact]
    public void UserCap_SpansDevices()
    {
        for (var device = 0; device < 5; device++)
        {
            for (var i = 0; i < 4; i++)
            {
                Assert.True(_throttle.TryBegin(User, $"device:{device}", out _));
                _throttle.RecordFailure(User, $"device:{device}", "kid");
            }
        }

        Assert.False(_throttle.TryBegin(User, "device:0", out var retry));
        Assert.Equal(30, retry);
        Assert.False(_throttle.TryBegin(User, "device:new", out _));
        Assert.True(_throttle.TryBegin("another kid", "device:0", out _));
    }

    [Fact]
    public void ChangingDevice_DoesNotResetTheUsersOwnCount_ButHasItsOwnPairCount()
    {
        Fail(5);

        Assert.False(_throttle.TryBegin(User, Device, out _));
        Assert.True(_throttle.TryBegin(User, "device:other", out _));
    }

    [Fact]
    public void IdleRecords_ArePruned()
    {
        Fail(5);
        Assert.True(_throttle.Count > 0);

        _time.Advance(TimeSpan.FromMinutes(31) + TimeSpan.FromSeconds(30));
        _throttle.TryBegin("someone else", Device, out _);

        Assert.Equal(0, _throttle.Count);
    }

    private void Fail(int times)
    {
        for (var i = 0; i < times; i++)
        {
            _throttle.RecordFailure(User, Device, "kid");
        }
    }
}
