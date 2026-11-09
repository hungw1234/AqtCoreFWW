using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Phase 5 Slice 2: <see cref="SignatureStampRasterizer"/> (Approach R) — renders a composed stamp to ONE
/// baseline JPEG via QuestPDF. The load-bearing assertion is that the output parses with the writer's
/// <see cref="JpegImageInfo"/> SOF0 parser: a progressive JPEG would be rejected downstream (fail-closed), so
/// "valid baseline JPEG" is the contract the writer depends on. Also proves Vietnamese diacritics co-render
/// with a real image without a QuestPDF layout/glyph error (Noto Sans coverage on the dev/CI host; the musl
/// render is verified at the P7 Alpine smoke step).
/// </summary>
public sealed class SignatureStampRasterizerTests
{
    private static readonly byte[] JpegSoi = [0xFF, 0xD8, 0xFF];

    [Fact]
    public void Rasterize_TextOnly_ProducesBaselineJpeg_AcceptedByWriterParser()
    {
        var stamp = new ComposedStamp(
            SignatureImage: [],
            TextLines: ["Ký bởi: Nguyễn Văn Đức", "Thời gian: 30/06/2026", "Lý do: Phê duyệt", "Nhà CC: CN=Test CA"]);

        byte[] jpeg = new SignatureStampRasterizer().Rasterize(stamp, 200f, 100f);

        jpeg.Take(3).Should().Equal(JpegSoi, "Approach R must emit a JPEG (SOI/marker)");
        // Teeth: the writer's SOF0 parser accepts it ⇒ baseline, not progressive (progressive fails closed).
        var info = JpegImageInfo.Parse(jpeg);
        info.Width.Should().BeGreaterThan(0);
        info.Height.Should().BeGreaterThan(0);
        info.ColorSpace.Should().Be("/DeviceRGB");
    }

    [Fact]
    public void Rasterize_WithImageAndDiacritics_Renders_WithoutLayoutOrGlyphError()
    {
        var stamp = new ComposedStamp(
            SignatureImage: OnePixelPng(),
            TextLines: ["Ký bởi: Đặng Thị Hưởng", "Lý do: Phê duyệt hồ sơ"]);

        byte[] jpeg = new SignatureStampRasterizer().Rasterize(stamp, 220f, 120f);

        JpegImageInfo.Parse(jpeg).Width.Should().BeGreaterThan(0);
        jpeg.Length.Should().BeGreaterThan(100, "a rendered stamp is non-trivial");
    }

    /// <summary>A valid 1×1 RGBA PNG — enough for QuestPDF's image decoder to exercise the image-embed path.</summary>
    private static byte[] OnePixelPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];
}
