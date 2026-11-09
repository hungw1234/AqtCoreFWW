using AqtCoreFW.Infrastructure.Export;
using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// SPIKE-C2 (Phase 5 gate): does QuestPDF `.GenerateImages()` work + render Vietnamese glyphs? QuestPDF
/// 2025.4 ships a single bundled native renderer (no separate SkiaSharp dependency), the same engine
/// `.GeneratePdf()` already uses successfully on the Alpine musl deploy target — so rastering the whole stamp
/// (user image + sanitized VN text) to ONE image and embedding it as a single PDF image XObject is far simpler
/// than hand-rolling TrueType CID-font embedding. This local spike proves the API shape + Noto-Sans VN glyph
/// render so Phase 5 (Approach R) can be built against a known-good surface; the musl-specific render is
/// verified at the P7 Alpine smoke step.
/// </summary>
public sealed class QuestPdfGenerateImagesSpikeC2Tests(ITestOutputHelper output)
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public void GenerateImages_RendersVietnameseStamp_ToNonEmptyPng()
    {
        // Community license must be set before generation; reading the const above does NOT run
        // QuestPdfExporter's static ctor, so set it explicitly here (the real Approach R service does the same).
        QuestPDF.Settings.License = LicenseType.Community;
        var fontFamily = QuestPdfExporter.VietnameseFontFamily;

        var images = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(220, 90, Unit.Point);
                page.Content().Column(col =>
                {
                    col.Item().Text("Ký bởi: Nguyễn Văn Đệ").FontFamily(fontFamily).FontSize(9);
                    col.Item().Text("Lý do: Phê duyệt hợp đồng").FontFamily(fontFamily).FontSize(8);
                    col.Item().Text("Serial: 01:AB:CD").FontFamily(fontFamily).FontSize(8);
                });
            });
        }).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 144,
        }).ToList();

        images.Should().ContainSingle("single-page stamp renders to one image");
        var png = images[0];
        png.Length.Should().BeGreaterThan(100, "a rendered stamp is non-trivial");
        png.Take(4).Should().Equal(PngMagic, "QuestPDF GenerateImages emits a real PNG");
        output.WriteLine($"SPIKE-C2 OK: GenerateImages produced {png.Length} bytes PNG.");
    }
}
