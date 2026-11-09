using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Infrastructure.Export;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// QuestPDF-backed <see cref="ISignatureStampRasterizer"/> (Approach R, sanctioned by SPIKE-C2): renders the
/// user's signature image plus the composed Vietnamese text lines into a single <b>baseline JPEG</b>. The
/// PAdES writer then embeds that one JPEG as a DCTDecode image XObject in the signature widget's appearance
/// stream and scales it 1:1 onto the chosen rectangle.
///
/// <para><b>Why a continuous page, not a fixed rect.</b> QuestPDF throws on content overflow rather than
/// shrinking, and a long issuer DN can wrap past a narrow stamp width. Rendering on a width-fixed,
/// height-flexible (<see cref="PageDescriptor.ContinuousSize(float, Unit)"/>) page guarantees no layout
/// exception; the writer's <c>cm</c> matrix re-scales the image to the exact widget rect, so the only
/// trade-off is a slight vertical squash when the content is taller than the rect's aspect — acceptable for a
/// stamp and far better than a crash on a real signing request.</para>
///
/// <para><b>License/font (SPIKE-C2 lesson).</b> Reading <see cref="QuestPdfExporter.VietnameseFontFamily"/> is
/// a compile-time-inlined constant and does NOT run the shared exporter's static ctor, so this class explicitly
/// calls <see cref="QuestPdfExporter.EnsureInitialized"/> to set the Community license + register the embedded
/// Noto Sans (Vietnamese glyph coverage on musl/Alpine).</para>
/// </summary>
public sealed class SignatureStampRasterizer : ISignatureStampRasterizer
{
    // points → pixels = points × 144/72 = ×2: legible on print without bloating the embedded JPEG.
    private const int RasterDpi = 144;
    private const float MarginPoints = 2f;

    /// <inheritdoc />
    public byte[] Rasterize(ComposedStamp stamp, float widthPoints, float heightPoints)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        if (widthPoints <= 0 || heightPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "Stamp rectangle must have positive extent.");

        // Reuse the shared exporter's Community license + embedded Noto registration (DRY). MUST be an explicit
        // call — reading the const family name alone does not trigger the static ctor.
        QuestPdfExporter.EnsureInitialized();

        IReadOnlyList<string> lines = stamp.TextLines ?? [];
        bool hasImage = stamp.SignatureImage is { Length: > 0 };

        // Heuristic font size from the rect width (~26 chars across), clamped to a legible range; wrapping is
        // safe here because the page height flows.
        float fontSize = Math.Clamp(widthPoints / 26f, 5f, 10f);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ContinuousSize(widthPoints, Unit.Point);
                page.MarginVertical(MarginPoints, Unit.Point);
                page.MarginHorizontal(MarginPoints, Unit.Point);
                page.PageColor(Colors.White); // opaque background: JPEG has no alpha (Approach R flatten)
                page.DefaultTextStyle(t => t.FontFamily(QuestPdfExporter.VietnameseFontFamily).FontSize(fontSize));

                page.Content().Column(col =>
                {
                    col.Spacing(1, Unit.Point);

                    if (hasImage)
                        col.Item().AlignCenter().Image(stamp.SignatureImage).FitWidth();

                    foreach (string line in lines)
                        col.Item().Text(line);
                });
            });
        });

        IEnumerable<byte[]> images = document.GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Jpeg,
            RasterDpi = RasterDpi,
            ImageCompressionQuality = ImageCompressionQuality.High,
        });

        return images.First();
    }
}
