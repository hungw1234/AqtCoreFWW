using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;

namespace AqtCoreFW.SharedKernel.Tests.Compliance;

/// <summary>
/// Unit tests for <see cref="PiiLogMasking"/> — call-site masking of Confidential PII (QĐ 742 §66)
/// before it reaches a log sink. Pins the security property: no readable local-part (email) or full
/// number (phone) ever appears in the output, while enough is kept for operator triage.
/// </summary>
public sealed class PiiLogMaskingTests
{
    // --- MaskEmail: keep first local char + full domain; leak nothing on malformed input ---

    [Theory]
    [InlineData("user@gov.vn", "u***@gov.vn")]
    [InlineData("a@gov.vn", "*@gov.vn")]                         // single-char local part → no char leaked
    [InlineData("john.doe@ministry.gov.vn", "j*******@ministry.gov.vn")]
    [InlineData("a@b@gov.vn", "*@b@gov.vn")]                     // multi-'@' → split at first, remainder kept as "domain"
    public void MaskEmail_KeepsFirstLocalCharAndFullDomain(string input, string expected)
    {
        PiiLogMasking.MaskEmail(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskEmail_NullOrWhitespace_ReturnsNoneSentinel(string? input)
    {
        PiiLogMasking.MaskEmail(input).Should().Be("(none)");
    }

    [Theory]
    [InlineData("notanemail", "**********")]                     // no '@' → mask whole string, leak nothing
    [InlineData("@gov.vn", "*******")]                           // '@' at index 0 → no local part → mask whole
    public void MaskEmail_Unparseable_MasksWholeString(string input, string expected)
    {
        PiiLogMasking.MaskEmail(input).Should().Be(expected);
    }

    // --- MaskPhone: keep last four digits ---

    [Theory]
    [InlineData("+84901234567", "********4567")]
    [InlineData("1234", "****")]                                 // exactly 4 → nothing kept
    [InlineData("123", "***")]                                   // fewer than 4 → mask all
    public void MaskPhone_KeepsLastFourDigits(string input, string expected)
    {
        PiiLogMasking.MaskPhone(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskPhone_NullOrWhitespace_ReturnsNoneSentinel(string? input)
    {
        PiiLogMasking.MaskPhone(input).Should().Be("(none)");
    }
}
