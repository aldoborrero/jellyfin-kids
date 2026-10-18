using Jellyfin.Plugin.Kids.ParentLock;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class ParentCodeTests
{
    [Theory]
    [InlineData("0000")]
    [InlineData("12345678901234567890123456789012")]
    public void Numeric_AcceptsDigits(string code)
    {
        Assert.Null(ParentCode.Validate(ParentLockType.Numeric, code));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123456789012345678901234567890123")]
    [InlineData("12a4")]
    [InlineData("UDLR")]
    [InlineData("")]
    [InlineData(null)]
    public void Numeric_RejectsOthers(string? code)
    {
        Assert.NotNull(ParentCode.Validate(ParentLockType.Numeric, code));
    }

    [Theory]
    [InlineData("UDLR")]
    [InlineData("rgyb")]
    [InlineData("pwf+-i09")]
    [InlineData("UDLRrg12")]
    public void Remote_AcceptsItsAlphabet(string code)
    {
        Assert.Null(ParentCode.Validate(ParentLockType.Remote, code));
    }

    [Theory]
    [InlineData("UDLX")]
    [InlineData("udlr")]
    [InlineData("R G1")]
    [InlineData("RGB1")]
    [InlineData("UDL")]
    public void Remote_RejectsOthersAndIsCaseSensitive(string code)
    {
        Assert.NotNull(ParentCode.Validate(ParentLockType.Remote, code));
    }

    [Theory]
    [InlineData("remote", ParentLockType.Remote)]
    [InlineData("NUMERIC", ParentLockType.Numeric)]
    [InlineData("Numeric", ParentLockType.Numeric)]
    public void LockType_ParsesIgnoringCase(string value, string expected)
    {
        Assert.True(ParentLockType.TryParse(value, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("CHALLENGE")]
    [InlineData("")]
    [InlineData(null)]
    public void LockType_RejectsOthers(string? value)
    {
        Assert.False(ParentLockType.TryParse(value, out _));
    }
}
