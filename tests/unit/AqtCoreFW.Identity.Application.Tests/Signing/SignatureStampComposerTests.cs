using AqtCoreFW.Identity.Application.Signing;

namespace AqtCoreFW.Identity.Application.Tests.Signing;

// Stamp composer is pure logic (sanitize + assemble) after the C2 no-raster pivot. These tests prove the
// security-critical sanitization (M4: control chars/newlines stripped, overflow ellipsized) and that
// Vietnamese diacritics survive at the string level. Glyph-coverage (does the font actually draw "ễ"?)
// belongs to P5 where the XObject is rendered — not assertable here.

public sealed class SignatureStampComposerTests
{
    private readonly ISignatureStampComposer _sut = new SignatureStampComposer();

    private static readonly byte[] Image = [0x89, 0x50, 0x4E, 0x47];

    private static StampContent Content(string name = "Nguyễn Văn Đệ", string? reason = "Phê duyệt hợp đồng") =>
        new(Image, name, new DateTimeOffset(2026, 6, 29, 14, 30, 0, TimeSpan.FromHours(7)),
            reason, "01:AB:CD", "CN=Viettel-CA, O=Viettel");

    [Fact]
    public void Compose_VietnameseName_PreservesDiacritics_AndPassesImageThrough()
    {
        var result = _sut.Compose(Content());

        result.SignatureImage.Should().BeSameAs(Image, "image bytes are passed through, not re-encoded (C2)");
        result.TextLines.Should().Contain("Ký bởi: Nguyễn Văn Đệ", "diacritics survive verbatim");
        result.TextLines.Should().Contain(l => l.StartsWith("Thời gian: 29/06/2026 14:30"));
        result.TextLines.Should().Contain("Lý do: Phê duyệt hợp đồng");
        result.TextLines.Should().Contain("Serial: 01:AB:CD");
        result.TextLines.Should().Contain("Nhà CC: CN=Viettel-CA, O=Viettel");
    }

    [Fact]
    public void Compose_NoReason_OmitsReasonLine()
    {
        var result = _sut.Compose(Content(reason: null));
        result.TextLines.Should().NotContain(l => l.StartsWith("Lý do:"));
    }

    [Fact]
    public void Compose_ReasonWithControlCharsAndNewlines_CollapsesToSingleSpaces()
    {
        var result = _sut.Compose(Content(reason: "Line1\r\nLine2\tTabbed Null"));

        var reasonLine = result.TextLines.Single(l => l.StartsWith("Lý do:"));
        // \r\n and \t each collapse to a single space; embedded runs never break the rendered line.
        reasonLine.Should().Be("Lý do: Line1 Line2 Tabbed Null");
        reasonLine.Should().NotContainAny("\r", "\n", "\t");
    }

    [Fact]
    public void Compose_OversizeField_IsEllipsized()
    {
        var longReason = new string('x', 2000);
        var result = _sut.Compose(Content(reason: longReason));

        var reasonValue = result.TextLines.Single(l => l.StartsWith("Lý do:"))["Lý do: ".Length..];
        reasonValue.Length.Should().BeLessThanOrEqualTo(80);
        reasonValue.Should().EndWith("…");
    }
}
