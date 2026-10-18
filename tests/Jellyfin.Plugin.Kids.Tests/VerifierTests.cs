using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Kids.ParentLock;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class VerifierTests
{
    // Verifiers in Wholphin's PasscodeHasher format with fixed salts. Produced with
    //   openssl kdf -keylen 32 -kdfopt digest:SHA256 -kdfopt pass:<code> -kdfopt hexsalt:<salt> -kdfopt iter:120000 PBKDF2
    // which is the derivation PasscodeHasher.kt implements (one PBKDF2-HMAC-SHA256 block over the UTF-8 code), and which
    // Wholphin's own PasscodeHasherTest checks against the JDK's PBKDF2WithHmacSHA256. The same openssl reproduces the
    // RFC 7914 vector below.
    public const string WholphinDigits2468 =
        "pbkdf2-sha256$120000$000102030405060708090a0b0c0d0e0f$802d419b302c354619eaf88ac67c57799883739f463759bd55f6b1662e24d65d";

    public const string WholphinRemoteUdlrRg12 =
        "pbkdf2-sha256$120000$f0e1d2c3b4a5968778695a4b3c2d1e0f$fa6a76745eeb53a863f4e826714952f77beb8536eaf52e9a7ee08dd5484df806";

    [Fact]
    public void Verify_Rfc7914Vector()
    {
        // RFC 7914 §11: PBKDF2-HMAC-SHA256 (P="passwd", S="salt", c=1), first 32 bytes
        const string Rfc = "pbkdf2-sha256$1$73616c74$55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc";

        Assert.True(Verifier.Verify("passwd", Rfc));
        Assert.False(Verifier.Verify("passwd2", Rfc));
    }

    [Theory]
    [InlineData(WholphinDigits2468, "2468", "2469")]
    [InlineData(WholphinRemoteUdlrRg12, "UDLRrg12", "UDLRrg13")]
    [InlineData(WholphinRemoteUdlrRg12, "UDLRrg12", "uDLRrg12")]
    public void Verify_WholphinVerifiers(string verifier, string code, string wrong)
    {
        Assert.True(Verifier.Verify(code, verifier));
        Assert.False(Verifier.Verify(wrong, verifier));
    }

    [Theory]
    [InlineData(WholphinDigits2468, "2468")]
    [InlineData(WholphinRemoteUdlrRg12, "UDLRrg12")]
    public void Fixtures_MatchAPortOfWholphinsDerivation(string verifier, string code)
    {
        var parts = verifier.Split('$');

        Assert.Equal(parts[3], Convert.ToHexStringLower(WholphinDerive(code, Convert.FromHexString(parts[2]), 120_000)));
    }

    [Fact]
    public void Verify_AcceptsUppercaseHex()
    {
        Assert.True(Verifier.Verify("2468", WholphinDigits2468.ToUpperInvariant().Replace("PBKDF2-SHA256", Verifier.Prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void Create_HasWholphinShapeAndIsSalted()
    {
        var a = Verifier.Create("UDLR12");
        var b = Verifier.Create("UDLR12");

        Assert.Matches(new Regex(@"^pbkdf2-sha256\$120000\$[0-9a-f]{32}\$[0-9a-f]{64}$"), a);
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("UDLR12", a, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ChecksAgainstAnIndependentDerivation()
    {
        var verifier = Verifier.Create("2468");
        var parts = verifier.Split('$');
        var salt = Convert.FromHexString(parts[2]);

        var iterations = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(parts[3], Convert.ToHexStringLower(WholphinDerive("2468", salt, iterations)));
        Assert.True(Verifier.Verify("2468", verifier));
    }

    // A port of PasscodeHasher.derive: one PBKDF2 block, U1 = HMAC(code, salt || 00000001), then XOR of the rounds
    private static byte[] WholphinDerive(string code, byte[] salt, int iterations)
    {
        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(code));
        var u = mac.ComputeHash([.. salt, 0, 0, 0, 1]);
        var result = (byte[])u.Clone();
        for (var i = 1; i < iterations; i++)
        {
            u = mac.ComputeHash(u);
            for (var j = 0; j < result.Length; j++)
            {
                result[j] ^= u[j];
            }
        }

        return result;
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pbkdf2-sha256$0$00$00")]
    [InlineData("pbkdf2-sha256$10$zz$00")]
    [InlineData("md5$1$00$00")]
    [InlineData("pbkdf2-sha256$1$00$")]
    [InlineData("pbkdf2-sha256$-5$00$00")]
    public void Verify_MalformedNeverVerifies(string verifier)
    {
        Assert.False(Verifier.Verify("1234", verifier));
        Assert.False(Verifier.Verify(string.Empty, verifier));
    }

    [Theory]
    [InlineData(10000, 16)]
    [InlineData(1000000, 64)]
    [InlineData(120000, 16)]
    public void ValidateForImport_AcceptsBounds(int iterations, int saltBytes)
    {
        Assert.Null(Verifier.ValidateForImport(Make(iterations, saltBytes, 32)));
    }

    [Theory]
    [InlineData(9999, 16, 32)]
    [InlineData(1000001, 16, 32)]
    [InlineData(120000, 15, 32)]
    [InlineData(120000, 65, 32)]
    [InlineData(120000, 16, 31)]
    [InlineData(120000, 16, 33)]
    public void ValidateForImport_RejectsOutOfRange(int iterations, int saltBytes, int hashBytes)
    {
        Assert.NotNull(Verifier.ValidateForImport(Make(iterations, saltBytes, hashBytes)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256$120000$00$00")]
    [InlineData("pbkdf2-sha256$120000$000102030405060708090a0b0c0d0e0f")]
    [InlineData("pbkdf2-sha256$120000$000102030405060708090a0b0c0d0e0f$00$00")]
    public void ValidateForImport_RejectsBadShape(string? verifier)
    {
        Assert.NotNull(Verifier.ValidateForImport(verifier));
    }

    [Fact]
    public void ValidateForImport_AcceptsWholphinVerifiers()
    {
        Assert.Null(Verifier.ValidateForImport(WholphinDigits2468));
        Assert.Null(Verifier.ValidateForImport(WholphinRemoteUdlrRg12));
    }

    private static string Make(int iterations, int saltBytes, int hashBytes)
        => $"pbkdf2-sha256${iterations}${new string('a', saltBytes * 2)}${new string('b', hashBytes * 2)}";
}
