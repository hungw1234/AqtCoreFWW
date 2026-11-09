using AqtCoreFW.SharedKernel.Application.Export;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AqtCoreFW.Infrastructure.Export;

/// <summary>
/// QuestPDF implementation of IPdfExporter.
/// Community license: free for internal GOV tools (revenue &lt; $1M).
/// Caller provides layout via Action&lt;IDocumentContainer&gt; for full type-safe fluent API.
/// Noto Sans font embedded for guaranteed Vietnamese rendering in containerized GOV environments
/// where system fonts may not include Vietnamese glyphs (Alpine Linux, locked-down hosts).
/// </summary>
public sealed class QuestPdfExporter : IPdfExporter
{
    /// <summary>Font family name available after registration. Use in QuestPDF .FontFamily() calls.</summary>
    public const string VietnameseFontFamily = "Noto Sans";

    static QuestPdfExporter()
    {
        // Community license — required call before any document generation
        QuestPDF.Settings.License = LicenseType.Community;

        // Register embedded Noto Sans for Vietnamese glyph coverage in containers
        using var stream = typeof(QuestPdfExporter).Assembly
            .GetManifestResourceStream("NotoSans-Regular.ttf");
        if (stream is not null)
            FontManager.RegisterFontWithCustomName(VietnameseFontFamily, stream);
    }

    /// <summary>
    /// No-op whose only purpose is to force this type's static constructor to run, which sets the QuestPDF
    /// Community license and registers the embedded Noto Sans font. Other QuestPDF callers in different
    /// assemblies (e.g. the signature-stamp rasterizer in Identity.Infrastructure) MUST invoke this before
    /// generating output: reading <see cref="VietnameseFontFamily"/> is a compile-time-inlined constant and
    /// does NOT trigger the static ctor, so the license/font setup would otherwise be skipped.
    /// </summary>
    public static void EnsureInitialized()
    {
        // Body intentionally empty — the static constructor does the work on first type access.
    }

    public Task<byte[]> ExportAsync(
        Action<object> compose,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Cast the generic compose action to the QuestPDF-typed action
        var typedCompose = compose as Action<IDocumentContainer>
            ?? throw new ArgumentException(
                "compose must be Action<IDocumentContainer> for QuestPdfExporter", nameof(compose));

        var bytes = Document.Create(typedCompose).GeneratePdf();
        return Task.FromResult(bytes);
    }
}
