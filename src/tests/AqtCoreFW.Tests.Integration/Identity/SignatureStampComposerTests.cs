using AqtCoreFW.Identity.Application.Signing;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Covers <see cref="SignatureStampComposer"/> line assembly — in particular that Reason, Serial and Nhà CC
/// are each OMITTED when their value is empty (the per-profile toggle passes an empty string to suppress a
/// line), while the always-present Ký bởi / Thời gian lines remain.
/// </summary>
public sealed class SignatureStampComposerTests
{
    private static readonly byte[] Img = [1, 2, 3];
    private static readonly DateTimeOffset SignedAt = new(2026, 7, 2, 19, 15, 0, TimeSpan.FromHours(7));
    private readonly SignatureStampComposer _composer = new();

    [Fact]
    public void Full_content_renders_all_five_lines()
    {
        var result = _composer.Compose(new StampContent(
            Img, "Nguyễn Văn Đệ", SignedAt, "Phê duyệt", "ABC123", "CN=Viettel-CA RS"));

        result.TextLines.Should().ContainInOrder(
            "Ký bởi: Nguyễn Văn Đệ",
            "Thời gian: 02/07/2026 19:15",
            "Lý do: Phê duyệt",
            "Serial: ABC123",
            "Nhà CC: CN=Viettel-CA RS");
    }

    [Fact]
    public void Empty_serial_and_issuer_omit_those_lines()
    {
        // Profile suppressed both Serial + Nhà CC → empty strings → lines dropped, not rendered as bare labels.
        var result = _composer.Compose(new StampContent(
            Img, "Nguyễn Văn Đệ", SignedAt, Reason: null, Serial: "", IssuerCa: ""));

        result.TextLines.Should().HaveCount(2);
        result.TextLines.Should().NotContain(l => l.StartsWith("Serial:"));
        result.TextLines.Should().NotContain(l => l.StartsWith("Nhà CC:"));
        result.TextLines.Should().NotContain(l => l.StartsWith("Lý do:"));
    }

    [Fact]
    public void Serial_shown_but_issuer_suppressed_renders_serial_only()
    {
        var result = _composer.Compose(new StampContent(
            Img, "ACME JSC", SignedAt, Reason: null, Serial: "DEAD01", IssuerCa: ""));

        result.TextLines.Should().Contain("Serial: DEAD01");
        result.TextLines.Should().NotContain(l => l.StartsWith("Nhà CC:"));
    }
}
