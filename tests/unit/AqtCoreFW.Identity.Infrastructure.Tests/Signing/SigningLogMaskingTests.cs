using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="SigningLogMasking"/> — ensures the signer CCCD (Confidential
/// PII, QĐ 742 §4.4.a) never reaches diagnostic logs in full.
/// </summary>
[Trait("Category", "SigningLogMasking")]
public sealed class SigningLogMaskingTests
{
    // -------------------------------------------------------------------------
    // 1. A full 12-digit CCCD keeps only the last 4 chars, prefixed with "***".
    // -------------------------------------------------------------------------
    [Fact]
    public void MaskUserId_FullCccd_KeepsOnlyLastFour()
    {
        var masked = SigningLogMasking.MaskUserId("079201001234");

        masked.Should().Be("***1234");
        masked.Should().NotContain("079201001"); // leading 8 digits never surface
    }

    // -------------------------------------------------------------------------
    // 2. Identifiers of 4 chars or fewer are masked entirely (cannot self-expose).
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("1234")]
    [InlineData("12")]
    [InlineData("x")]
    public void MaskUserId_ShortIdentifier_MaskedEntirely(string userId)
    {
        SigningLogMasking.MaskUserId(userId).Should().Be("****");
    }

    // -------------------------------------------------------------------------
    // 3. Null / empty → "(none)" so logs distinguish missing from masked.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MaskUserId_NullOrEmpty_ReturnsNoneSentinel(string? userId)
    {
        SigningLogMasking.MaskUserId(userId).Should().Be("(none)");
    }

    // -------------------------------------------------------------------------
    // 4. A 5-char identifier (just over the threshold) reveals exactly 4 chars.
    // -------------------------------------------------------------------------
    [Fact]
    public void MaskUserId_FiveChars_RevealsLastFourOnly()
    {
        SigningLogMasking.MaskUserId("a1234").Should().Be("***1234");
    }
}
