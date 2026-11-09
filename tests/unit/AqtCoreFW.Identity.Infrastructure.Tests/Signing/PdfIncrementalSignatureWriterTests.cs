using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using AqtCoreFW.Identity.Infrastructure.Tests.Signing.Fixtures;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Pins the incremental-update writer's structural invariants — the offset/ByteRange arithmetic that, when wrong,
/// makes Adobe report "document has been altered". Plus the S1 parser-hardening guarantees: unsupported/malformed
/// input fails closed (no signature) rather than producing a mis-signed PDF.
/// </summary>
public sealed class PdfIncrementalSignatureWriterTests
{
    private static readonly PdfIncrementalSignatureWriter Writer = new();

    [Fact]
    public void Prepare_ByteRange_BracketsContentsGapExactly()
    {
        byte[] pdf = MinimalPdfFactory.CreateSinglePage();
        var prep = Writer.Prepare(pdf, contentsHexLength: 8192);

        int[] br = prep.ByteRange;
        br[0].Should().Be(0);
        // range1 ends ON the '<' (its last byte); range2 starts ON the '>'.
        prep.PreparedPdf[br[1] - 1].Should().Be((byte)'<');
        prep.PreparedPdf[br[2]].Should().Be((byte)'>');
        // The excluded gap is exactly the hex placeholder width.
        (br[2] - br[1]).Should().Be(prep.ContentsHexLength);
        // ByteRange covers the entire file outside the gap.
        (br[0] + br[1] + (prep.PreparedPdf.Length - br[2])).Should().Be(prep.PreparedPdf.Length - prep.ContentsHexLength);
    }

    [Fact]
    public void Prepare_DocumentDigest_RecomputesOverByteRange()
    {
        byte[] pdf = MinimalPdfFactory.CreateSinglePage();
        var prep = Writer.Prepare(pdf, contentsHexLength: 8192);

        using var sha = SHA256.Create();
        sha.TransformBlock(prep.PreparedPdf, prep.ByteRange[0], prep.ByteRange[1], null, 0);
        sha.TransformFinalBlock(prep.PreparedPdf, prep.ByteRange[2], prep.ByteRange[3]);
        prep.DocumentDigest.Should().Equal(sha.Hash!, because: "the digest must cover exactly the ByteRange bytes");
    }

    [Fact]
    public void Prepare_WrittenByteRange_ParsesToTheReportedInts()
    {
        byte[] pdf = MinimalPdfFactory.CreateSinglePage();
        var prep = Writer.Prepare(pdf, contentsHexLength: 4096);

        string text = Encoding.Latin1.GetString(prep.PreparedPdf);
        var m = Regex.Match(text, @"/ByteRange \[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        m.Success.Should().BeTrue();
        int[] parsed = [int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                        int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)];
        parsed.Should().Equal(prep.ByteRange, because: "the in-PDF ByteRange must match the digest's ByteRange");
    }

    [Fact]
    public void Prepare_IsIncrementalUpdate_OriginalBytesArePrefixStable()
    {
        byte[] pdf = MinimalPdfFactory.CreateSinglePage();
        var prep = Writer.Prepare(pdf);

        prep.PreparedPdf.Length.Should().BeGreaterThan(pdf.Length);
        prep.PreparedPdf.AsSpan(0, pdf.Length).SequenceEqual(pdf).Should().BeTrue(
            because: "incremental update appends only; the original bytes must be a byte-identical prefix");
    }

    [Fact]
    public void EmbedCms_FitsPlaceholder_AndPreservesLengthAndDigest()
    {
        byte[] pdf = MinimalPdfFactory.CreateSinglePage();
        var prep = Writer.Prepare(pdf, contentsHexLength: 8192);
        byte[] cms = RandomNumberGenerator.GetBytes(1500);

        byte[] signed = Writer.EmbedCms(prep, cms);

        signed.Length.Should().Be(prep.PreparedPdf.Length, because: "splice is length-preserving");
        // Re-hashing the ByteRange of the SIGNED pdf still reproduces the digest (the gap is outside the ranges).
        using var sha = SHA256.Create();
        sha.TransformBlock(signed, prep.ByteRange[0], prep.ByteRange[1], null, 0);
        sha.TransformFinalBlock(signed, prep.ByteRange[2], prep.ByteRange[3]);
        sha.Hash!.Should().Equal(prep.DocumentDigest);
        // The CMS hex landed in the gap.
        Encoding.Latin1.GetString(signed, prep.ContentsGapStart, cms.Length * 2)
            .Should().Be(Convert.ToHexString(cms));
    }

    [Fact]
    public void EmbedCms_OverflowingPlaceholder_FailsClosed()
    {
        byte[] pdf = MinimalPdfFactory.CreateSinglePage();
        var prep = Writer.Prepare(pdf, contentsHexLength: 512);
        byte[] tooBig = RandomNumberGenerator.GetBytes(1000); // 2000 hex chars > 512

        Action act = () => Writer.EmbedCms(prep, tooBig);
        act.Should().Throw<PdfSigningException>(because: "an oversized CMS must fail closed, never truncate the signature");
    }

    [Theory]
    [InlineData("not a pdf at all")]
    [InlineData("%PDF-1.7\ntruncated no xref")]
    public void Prepare_MalformedInput_FailsClosed(string raw)
    {
        Action act = () => Writer.Prepare(Encoding.Latin1.GetBytes(raw.PadRight(64)));
        act.Should().Throw<PdfSigningException>(because: "S1: untrusted/malformed PDFs must fail closed, not mis-sign");
    }

    [Fact]
    public void Prepare_NestedPageTree_FailsClosed()
    {
        // First /Kids entry resolves to a /Pages node, not a leaf /Page → signing it would mis-structure the PDF.
        Action act = () => Writer.Prepare(MinimalPdfFactory.CreateNestedPageTree());
        act.Should().Throw<PdfSigningException>(because: "S1: a non-leaf first page must fail closed, not be signed");
    }

    [Fact]
    public void Prepare_InheritedAnnotsOnPageTree_FailsClosed()
    {
        // The page-tree node carries inherited /Annots the writer's rewrite would silently drop → fail closed.
        Action act = () => Writer.Prepare(MinimalPdfFactory.CreateWithInheritedAnnots());
        act.Should().Throw<PdfSigningException>(because: "S1: dropping inherited annotations is a silent corruption");
    }

    [Fact]
    public void Prepare_ExistingAcroForm_FailsClosed()
    {
        byte[] pdf = MinimalPdfFactory.CreateSinglePage();
        // Inject an /AcroForm into the catalog to simulate an already-formed PDF the spike does not merge.
        string text = Encoding.Latin1.GetString(pdf).Replace(
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Catalog /Pages 2 0 R /AcroForm 9 0 R >>");
        Action act = () => Writer.Prepare(Encoding.Latin1.GetBytes(text));
        act.Should().Throw<PdfSigningException>();
    }
}
