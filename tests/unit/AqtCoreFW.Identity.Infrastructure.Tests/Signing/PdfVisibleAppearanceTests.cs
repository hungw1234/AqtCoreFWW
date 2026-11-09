using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using AqtCoreFW.Identity.Infrastructure.Tests.Signing.Fixtures;
using FluentAssertions;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.X509;
using Xunit;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Phase 5 Slice 1: visible-appearance support on <see cref="PdfIncrementalSignatureWriter"/>. The load-bearing
/// test is <see cref="SignWithAppearance_SignatureStillVerifies"/> — the appearance bytes are inside the signed
/// ByteRange, so embedding a stamp must NOT break the PAdES signature (the project's death-trap is a green
/// self-test + Adobe-invalid signature). The rest pin the red-team must-fixes: invariant numerics (vi-VN),
/// MediaBox-bounded placement, JPEG-SOF-derived dict, and a byte-identical invisible path.
/// </summary>
public sealed class PdfVisibleAppearanceTests
{
    private static SignatureAppearance Appearance(byte[]? jpeg = null,
        float x = 72, float y = 72, float w = 200, float h = 100, int page = 1) =>
        new(jpeg ?? JpegFixtures.BaselineRgb(200, 100), x, y, w, h, page);

    // ── Multi-page: a page-2 stamp targets the second page object, not the first ──────────────────
    [Fact]
    public void Appearance_OnSecondPage_WidgetTargetsSecondPageObject()
    {
        // Two-page flat tree: page 1 = obj 3, page 2 = obj 4. The widget must reference obj 4 and the page-4
        // object (not page-3) must gain /Annots — otherwise the stamp lands on the wrong page (review C1).
        byte[] inputPdf = MinimalPdfFactory.CreateTwoPage();
        var prep = new PdfIncrementalSignatureWriter().Prepare(inputPdf, 32 * 1024, Appearance(page: 2));

        // The appended incremental section only (after the original prefix) is where the rewrites live.
        string appended = Encoding.Latin1.GetString(
            prep.PreparedPdf, inputPdf.Length, prep.PreparedPdf.Length - inputPdf.Length);

        appended.Should().Contain("/P 4 0 R", "the signature widget must be parented to the page-2 object");
        appended.Should().MatchRegex(@"4 0 obj[\s\S]*?/Annots \[", "page 2's object must gain the /Annots entry");
        appended.Should().NotContain("3 0 obj", "page 1 must not be re-emitted/altered when stamping page 2");
    }

    [Fact]
    public void Appearance_FirstPageOnTwoPageDoc_TargetsFirstPageObject()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateTwoPage();
        var prep = new PdfIncrementalSignatureWriter().Prepare(inputPdf, 32 * 1024, Appearance(page: 1));
        string appended = Encoding.Latin1.GetString(
            prep.PreparedPdf, inputPdf.Length, prep.PreparedPdf.Length - inputPdf.Length);

        appended.Should().Contain("/P 3 0 R", "page 1 stamp must be parented to the page-1 object (obj 3)");
        appended.Should().NotContain("4 0 obj", "page 2 must not be re-emitted when stamping page 1");
    }

    [Fact]
    public void Appearance_PageOutOfRange_FailsClosed()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateTwoPage(); // 2 pages
        var act = () => new PdfIncrementalSignatureWriter().Prepare(inputPdf, 32 * 1024, Appearance(page: 3));
        act.Should().Throw<PdfSigningException>("a page beyond the document must fail closed, never silently stamp page 1");
    }

    // ── Teeth: signature stays valid with the appearance in the signed range ─────────────────────
    [Fact]
    public void SignWithAppearance_SignatureStillVerifies()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateSinglePage("PAdES visible-stamp");
        var writer = new PdfIncrementalSignatureWriter();
        var cms = new PadesCmsBuilder();
        var signer = new LocalRsaHashSigner();

        var prep = writer.Prepare(inputPdf, 32 * 1024, Appearance());
        var attrs = cms.BuildSignedAttributes(prep.DocumentDigest, signer.SignerChain[0], DateTime.UtcNow);
        byte[] rawSig = signer.Sign(attrs.SignInput);
        byte[] cmsDer = cms.AssembleDetachedCms(attrs, rawSig, signer.SignerChain);
        byte[] signedPdf = writer.EmbedCms(prep, cmsDer);

        var (byteRange, contentsCms) = ReadSignature(signedPdf);
        byte[] covered = ConcatRanges(signedPdf, byteRange);

        SHA256.HashData(covered).Should().Equal(prep.DocumentDigest,
            "the signed PDF's ByteRange (which now includes the stamp objects) must reproduce messageDigest");

        var signedData = new CmsSignedData(new CmsProcessableByteArray(covered), contentsCms);
        var certStore = signedData.GetCertificates();
        foreach (SignerInformation si in signedData.GetSignerInfos().GetSigners())
        {
            BcX509.X509Certificate cert = certStore.EnumerateMatches(si.SignerID).First();
            si.Verify(cert).Should().BeTrue("embedding a visible appearance must not invalidate the PAdES signature");
        }
    }

    // ── Visible structure: image XObject + /AP + non-zero /Rect present ──────────────────────────
    [Fact]
    public void Appearance_EmitsImageXObject_FormAp_AndNonZeroRect()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateSinglePage();
        var prep = new PdfIncrementalSignatureWriter().Prepare(inputPdf, 32 * 1024, Appearance(x: 100, y: 150, w: 180, h: 90));
        string text = Encoding.Latin1.GetString(prep.PreparedPdf);

        text.Should().Contain("/Subtype /Image").And.Contain("/Filter /DCTDecode");
        text.Should().Contain("/AP << /N");
        text.Should().Contain("/Rect [100 150 280 240]", "rect = [x y x+w y+h] in invariant points");
        text.Should().NotContain("/Rect [0 0 0 0]", "the visible widget must not stay invisible");
    }

    // ── Backward-compat: invisible path byte-identical to the original two-arg Prepare (M5) ───────
    [Fact]
    public void NullAppearance_IsByteIdentical_ToTwoArgPrepare()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateSinglePage();
        var writer = new PdfIncrementalSignatureWriter();

        // Both produce a 'D:...' timestamp from UtcNow; compare structure by neutralising only that field.
        byte[] a = writer.Prepare(inputPdf, 32 * 1024).PreparedPdf;
        byte[] b = writer.Prepare(inputPdf, 32 * 1024, null).PreparedPdf;

        Normalize(a).Should().Equal(Normalize(b), "the null-appearance path must match the original invisible path");
        Encoding.Latin1.GetString(a).Should().NotContain("/AP", "the invisible path emits no appearance stream");

        static byte[] Normalize(byte[] pdf) =>
            Encoding.Latin1.GetBytes(Regex.Replace(Encoding.Latin1.GetString(pdf), @"/M \(D:[^)]*\)", "/M (D:X)"));
    }

    // ── C1: invariant numerics — a vi-VN host must not emit a comma decimal that corrupts /Rect ──
    [Fact]
    public void ViVnCulture_EmitsDotDecimal_NotComma()
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("vi-VN");
            byte[] inputPdf = MinimalPdfFactory.CreateSinglePage();
            var prep = new PdfIncrementalSignatureWriter()
                .Prepare(inputPdf, 32 * 1024, Appearance(x: 72.5f, y: 72f, w: 100.25f, h: 50f));
            string text = Encoding.Latin1.GetString(prep.PreparedPdf);

            var rect = Regex.Match(text, @"/Rect \[[^\]]*\]").Value;
            rect.Should().Contain("72.5").And.NotContain(",", "PDF reals must be dot-decimal regardless of host locale");
            Regex.Match(text, @"/BBox \[[^\]]*\]").Value.Should().NotContain(",");
            // The content-stream `cm` operands also carry reals (100.25) — assert they too are dot-decimal.
            Regex.Match(text, @"q [^Q]*cm").Value.Should().Contain("100.25").And.NotContain(",");
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    // ── C2: bounds honour a non-zero MediaBox origin (llx/lly, not 0) ────────────────────────────
    [Fact]
    public void NonZeroOriginMediaBox_BoundsAgainstLlxLly()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateSinglePageWithMediaBox("[10 10 622 802]");
        var writer = new PdfIncrementalSignatureWriter();

        // A rect below the origin (x0=5 < llx=10) must be rejected even though it is > 0.
        var below = () => writer.Prepare(inputPdf, 32 * 1024, Appearance(x: 5, y: 50, w: 100, h: 50));
        below.Should().Throw<PdfSigningException>().WithMessage("*MediaBox*");

        // A rect fully inside the shifted box is accepted.
        var inside = writer.Prepare(inputPdf, 32 * 1024, Appearance(x: 20, y: 20, w: 100, h: 50));
        Encoding.Latin1.GetString(inside.PreparedPdf).Should().Contain("/Rect [20 20 120 70]");
    }

    // ── C2: placement is bounded by the page MediaBox ([0 0 612 792]) ────────────────────────────
    [Fact]
    public void RectOutsideMediaBox_FailsClosed()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateSinglePage();
        var writer = new PdfIncrementalSignatureWriter();
        var act = () => writer.Prepare(inputPdf, 32 * 1024, Appearance(x: 500, y: 72, w: 200, h: 50)); // x1=700 > 612

        act.Should().Throw<PdfSigningException>().WithMessage("*MediaBox*");
    }

    // ── H1/H2: ColorSpace derived from the JPEG SOF (single source of truth) ──────────────────────
    [Fact]
    public void GrayscaleJpeg_DerivesDeviceGray()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateSinglePage();
        var prep = new PdfIncrementalSignatureWriter()
            .Prepare(inputPdf, 32 * 1024, Appearance(jpeg: JpegFixtures.BaselineGray(64, 64)));
        Encoding.Latin1.GetString(prep.PreparedPdf).Should().Contain("/ColorSpace /DeviceGray");
    }

    // ── M1/L2: non-baseline / non-JPEG fail closed ───────────────────────────────────────────────
    [Fact]
    public void ProgressiveJpeg_FailsClosed()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateSinglePage();
        var act = () => new PdfIncrementalSignatureWriter()
            .Prepare(inputPdf, 32 * 1024, Appearance(jpeg: JpegFixtures.Progressive(64, 64)));
        act.Should().Throw<PdfSigningException>().WithMessage("*rogressive*");
    }

    [Fact]
    public void NonJpegBytes_FailClosed()
    {
        byte[] inputPdf = MinimalPdfFactory.CreateSinglePage();
        var act = () => new PdfIncrementalSignatureWriter()
            .Prepare(inputPdf, 32 * 1024, Appearance(jpeg: [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A])); // PNG magic
        act.Should().Throw<PdfSigningException>().WithMessage("*JPEG*");
    }

    // ── Verify helpers (mirror PadesSpikeEndToEndTests) ──────────────────────────────────────────
    private static (int[] byteRange, byte[] cms) ReadSignature(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var br = Regex.Match(text, @"/ByteRange \[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        br.Success.Should().BeTrue();
        int[] range = [int.Parse(br.Groups[1].Value), int.Parse(br.Groups[2].Value),
                       int.Parse(br.Groups[3].Value), int.Parse(br.Groups[4].Value)];
        int open = text.IndexOf("/Contents <", StringComparison.Ordinal) + "/Contents <".Length;
        int close = text.IndexOf('>', open);
        using var ais = new Asn1InputStream(Convert.FromHexString(text[open..close]));
        return (range, ais.ReadObject().GetEncoded("DER"));
    }

    private static byte[] ConcatRanges(byte[] pdf, int[] br)
    {
        byte[] result = new byte[br[1] + br[3]];
        Buffer.BlockCopy(pdf, br[0], result, 0, br[1]);
        Buffer.BlockCopy(pdf, br[2], result, br[1], br[3]);
        return result;
    }
}

/// <summary>Synthetic JPEGs carrying just a valid frame header — enough for the SOF parser + DCTDecode embed
/// (the signature tests never render the image, so the entropy data is unnecessary).</summary>
internal static class JpegFixtures
{
    public static byte[] BaselineRgb(int w, int h) => Sof(0xC0, w, h, 3);
    public static byte[] BaselineGray(int w, int h) => Sof(0xC0, w, h, 1);
    public static byte[] Progressive(int w, int h) => Sof(0xC2, w, h, 3);

    private static byte[] Sof(byte sofMarker, int w, int h, int components)
    {
        var b = new List<byte> { 0xFF, 0xD8 };          // SOI
        int segLen = 8 + components * 3;                // len(2)+prec(1)+h(2)+w(2)+nf(1)+comps
        b.AddRange([0xFF, sofMarker, (byte)(segLen >> 8), (byte)(segLen & 0xFF), 0x08,
                    (byte)(h >> 8), (byte)(h & 0xFF), (byte)(w >> 8), (byte)(w & 0xFF), (byte)components]);
        for (int i = 1; i <= components; i++) b.AddRange([(byte)i, 0x11, 0x00]); // id, sampling, quant-table
        b.AddRange([0xFF, 0xD9]);                        // EOI
        return [.. b];
    }
}
