using System.Text;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Viettel's <c>document_name</c> must Base64-decode to text containing ONLY [A-Za-z0-9], space,
/// underscore and hyphen, with the Base64 string under 100 chars. A Vietnamese reason encoded
/// verbatim violates this and reproduces HTTP 400 error 58100 — exactly the bug being fixed.
/// These pin the sanitizer's invariants by decoding its output and asserting the constraints.
/// </summary>
public sealed class MysignDocumentNameTests
{
    private static string Decode(string encoded) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Encode_NullOrBlank_FallsBackToDefault(string? reason)
    {
        Decode(MysignDocumentName.Encode(reason)).Should().Be("Document");
    }

    [Fact]
    public void Encode_StripsVietnameseDiacritics()
    {
        // "Ký tài liệu demo" -> diacritics removed, charset preserved.
        var decoded = Decode(MysignDocumentName.Encode("Ký tài liệu demo"));

        decoded.Should().Be("Ky tai lieu demo");
    }

    [Fact]
    public void Encode_FoldsDStroke_NotDropped()
    {
        // "đ"/"Đ" has no NFD decomposition; it must fold to d/D, not vanish ("Sua doi", not "Sua oi").
        Decode(MysignDocumentName.Encode("Sửa đổi Đơn")).Should().Be("Sua doi Don");
    }

    [Fact]
    public void Encode_DropsDisallowedCharacters()
    {
        // Punctuation outside [_- ] and symbols must be removed, not encoded.
        var decoded = Decode(MysignDocumentName.Encode("Invoice #123 / 2026 (final)!"));

        decoded.Should().Be("Invoice 123  2026 final");
        decoded.Should().MatchRegex("^[A-Za-z0-9 _-]*$");
    }

    [Fact]
    public void Encode_AllDisallowed_FallsBackToDefault()
    {
        // Nothing survives sanitization → must not emit an empty document_name.
        Decode(MysignDocumentName.Encode("！＠＃％")).Should().Be("Document");
    }

    [Fact]
    public void Encode_TruncatesLongInput_KeepingBase64UnderCeiling()
    {
        var encoded = MysignDocumentName.Encode(new string('a', 500));

        encoded.Length.Should().BeLessThan(100, "Viettel caps the Base64 string under 100 chars");
        Decode(encoded).Should().MatchRegex("^a+$");
    }
}
