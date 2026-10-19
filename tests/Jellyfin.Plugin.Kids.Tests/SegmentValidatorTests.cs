using System;
using System.Linq;
using Jellyfin.Plugin.Kids.Allowance;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class SegmentValidatorTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static SegmentInput Valid(string id = "a") => new(id, _now.AddHours(-1), 600);

    [Fact]
    public void OneValidSegment_IsValid()
    {
        Assert.Empty(SegmentValidator.Validate([Valid()], _now));
    }

    [Fact]
    public void NoSegments_OrTooMany_AreRejected()
    {
        Assert.Contains("Segments", SegmentValidator.Validate([], _now).Keys);
        Assert.Contains("Segments", SegmentValidator.Validate(null, _now).Keys);
        Assert.Contains("Segments", SegmentValidator.Validate([.. Enumerable.Range(0, 101).Select(i => Valid($"s{i}"))], _now).Keys);
        Assert.Empty(SegmentValidator.Validate([.. Enumerable.Range(0, 100).Select(i => Valid($"s{i}"))], _now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingId_IsRejected(string? id)
    {
        Assert.Contains("Segments[0].Id", SegmentValidator.Validate([Valid() with { Id = id }], _now).Keys);
    }

    [Fact]
    public void IdLength_IsAtMost64()
    {
        Assert.Contains("Segments[0].Id", SegmentValidator.Validate([Valid(new string('x', 65))], _now).Keys);
        Assert.Empty(SegmentValidator.Validate([Valid(new string('x', 64))], _now));
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var errors = SegmentValidator.Validate([Valid("a"), Valid("b"), Valid("a")], _now);

        Assert.Equal(["Segments[2].Id"], errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(43_201)]
    [InlineData(null)]
    public void Seconds_OutOfRange_IsRejected(int? seconds)
    {
        Assert.Contains("Segments[0].Seconds", SegmentValidator.Validate([Valid() with { Seconds = seconds }], _now).Keys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(43_200)]
    public void Seconds_AtBounds_IsAccepted(int seconds)
    {
        Assert.Empty(SegmentValidator.Validate([Valid() with { Seconds = seconds }], _now));
    }

    [Fact]
    public void StartedAt_MustBeKnownRecentAndNotTooFarAhead()
    {
        Assert.Contains("Segments[0].StartedAt", SegmentValidator.Validate([Valid() with { StartedAt = null }], _now).Keys);
        Assert.Contains("Segments[0].StartedAt", SegmentValidator.Validate([Valid() with { StartedAt = _now.AddMinutes(5).AddSeconds(1) }], _now).Keys);
        Assert.Contains("Segments[0].StartedAt", SegmentValidator.Validate([Valid() with { StartedAt = _now.AddDays(-90).AddSeconds(-1) }], _now).Keys);
        Assert.Empty(SegmentValidator.Validate([Valid() with { StartedAt = _now.AddMinutes(5) }], _now));
        Assert.Empty(SegmentValidator.Validate([Valid() with { StartedAt = _now.AddDays(-90) }], _now));
    }

    [Fact]
    public void OneInvalidAmongValid_ReportsOnlyThatIndex()
    {
        var errors = SegmentValidator.Validate([Valid("a"), Valid("b") with { Seconds = 0 }, Valid("c")], _now);

        Assert.Equal(["Segments[1].Seconds"], errors.Keys);
    }
}
