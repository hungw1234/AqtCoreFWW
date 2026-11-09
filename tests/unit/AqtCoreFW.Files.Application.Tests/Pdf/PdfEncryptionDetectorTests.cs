using AqtCoreFW.Files.Application.Pdf;

namespace AqtCoreFW.Files.Application.Tests.Pdf;

/// <summary>
/// Unit tests for <see cref="PdfEncryptionDetector"/> — the ingest gate that keeps owner-password
/// protected PDFs (which the USB signing tool cannot open) out of the signing pipeline.
/// </summary>
public sealed class PdfEncryptionDetectorTests
{
    [Fact]
    public void IsEncrypted_TrailerWithEncryptReference_True()
    {
        var bytes = "%PDF-1.7\n1 0 obj<<>>endobj\ntrailer<< /Root 1 0 R /Encrypt 5 0 R >>\n%%EOF"u8.ToArray();

        PdfEncryptionDetector.IsEncrypted(bytes).Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_CleanPdf_NoEncryptEntry_False()
    {
        var bytes = "%PDF-1.7\n1 0 obj<<>>endobj\ntrailer<< /Root 1 0 R /Size 6 >>\n%%EOF"u8.ToArray();

        PdfEncryptionDetector.IsEncrypted(bytes).Should().BeFalse();
    }

    [Fact]
    public void IsEncrypted_EncryptAsBareNameNotReference_False()
    {
        // "/Encrypt" appearing as a plain name (no following object-number reference) must not trip the
        // gate — only the indirect-reference form the trailer uses counts.
        var bytes = "%PDF-1.7\n/Encrypt true /Other 1 0 R\n%%EOF"u8.ToArray();

        PdfEncryptionDetector.IsEncrypted(bytes).Should().BeFalse();
    }

    [Fact]
    public void IsEncrypted_EncryptReferenceWithExtraWhitespace_True()
    {
        var bytes = "trailer\n<<\n  /Encrypt   12 0 R\n  /Root 1 0 R\n>>"u8.ToArray();

        PdfEncryptionDetector.IsEncrypted(bytes).Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_Empty_False()
    {
        PdfEncryptionDetector.IsEncrypted(ReadOnlySpan<byte>.Empty).Should().BeFalse();
    }
}
