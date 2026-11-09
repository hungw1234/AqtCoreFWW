using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>
/// The hard "80%": appends a PAdES signature to a PDF as an <b>incremental update</b> (the original bytes are never
/// rewritten — they remain a byte-identical prefix). Two-stage to mirror the eventual Mysign two-call split:
/// <see cref="Prepare"/> lays out the signature dict + AcroForm field + fixed-width <c>/ByteRange</c> and a zero
/// <c>/Contents</c> placeholder, then computes the document digest over the byte ranges; <see cref="EmbedCms"/>
/// splices the finished CMS into the placeholder without shifting a single offset.
///
/// <para>The placeholder is sized for the REAL Mysign chain (default 32&#160;KB hex), not the small local cert, so
/// Phase 3 does not overflow a gap sized for the spike's self-signed leaf.</para>
/// </summary>
public sealed class PdfIncrementalSignatureWriter
{
    // Fixed-width reserved region for "0 len1 start2 len2"; written left-justified, space-padded, so the array's
    // byte length is invariant between the placeholder pass and the real-values pass (the classic ByteRange pitfall).
    private const int ByteRangeReserved = 40;

    // Server-rendered stamp JPEG is small; cap the embedded appearance to keep the appended section bounded.
    private const int MaxAppearanceJpegBytes = 2 * 1024 * 1024;

    public PdfSigningPreparation Prepare(
        byte[] inputPdf, int contentsHexLength = 32 * 1024, SignatureAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(inputPdf);
        if (contentsHexLength is < 256 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(contentsHexLength));

        var model = PdfDocumentModel.Parse(inputPdf);
        int catalogNum = model.CatalogObjectNumber;
        // Visible path targets the appearance's (1-based) page; invisible path stays on the first page (golden bytes).
        // PageObjectNumber(1) delegates to FirstPageObjectNumber, so the page-1 visible output is unchanged.
        bool visible = appearance is not null;
        int pageNum = visible ? model.PageObjectNumber(appearance!.Page) : model.FirstPageObjectNumber();
        string catalogDict = model.GetObjectDict(catalogNum);
        string pageDict = model.GetObjectDict(pageNum);
        if (pageDict.Contains("/Annots", StringComparison.Ordinal))
            throw new PdfSigningException("Target page already has /Annots; spike writer does not merge existing annotations.");

        // Object numbering: the invisible path keeps the original acroForm=Size, widget=Size+1, sig=Size+2 layout
        // (golden-bytes stable). The visible path prepends the image + form XObjects, shifting the rest up by two.
        int next = model.Size;
        int imageNum = visible ? next++ : 0;
        int formNum  = visible ? next++ : 0;
        int acroFormNum = next++, widgetNum = next++, sigNum = next++, newSize = next;

        // Build the visible-appearance objects (validates the JPEG + rect against the page MediaBox, fail-closed).
        // When absent, the widget stays invisible (/Rect [0 0 0 0], no /AP) — byte-identical to the original path.
        AppearanceObjects? appr = visible ? BuildAppearance(model, pageNum, appearance!, imageNum) : null;

        string byteRangePlaceholder = "/ByteRange [" + new string(' ', ByteRangeReserved) + "]";
        string contentsPlaceholder = "/Contents <" + new string('0', contentsHexLength) + ">";
        string m = "D:" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+00'00'";

        // /Contents is LAST so the only '<' after the /Contents token is the hex-string opener and the only '>'
        // before the closing '>>' is the hex-string closer.
        string sigDict =
            $"<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /ETSI.CAdES.detached /M ({m}) " +
            $"{byteRangePlaceholder} {contentsPlaceholder} >>";
        // /Rect + /AP are the only widget differences between the invisible and visible paths; for the invisible
        // path widgetRect="[0 0 0 0]" and widgetAp="" reproduce the original dict byte-for-byte.
        string widgetRect = appr?.Rect ?? "[0 0 0 0]";
        string widgetAp = appr is null ? "" : $"/AP << /N {formNum} 0 R >> ";
        string widgetDict =
            $"<< /Type /Annot /Subtype /Widget /FT /Sig /Rect {widgetRect} /T (Signature1) /F 132 " +
            $"{widgetAp}/P {pageNum} 0 R /V {sigNum} 0 R >>";
        string acroFormDict = $"<< /Fields [{widgetNum} 0 R] /SigFlags 3 >>";
        string catalogUpdated = InjectBeforeDictEnd(catalogDict, $"/AcroForm {acroFormNum} 0 R ");
        string pageUpdated = InjectBeforeDictEnd(pageDict, $"/Annots [{widgetNum} 0 R] ");

        // Assemble the appended section, recording each object's ABSOLUTE start offset for the xref table. The sig
        // object is written FIRST (lowest offset) so the /Contents, /ByteRange and hex-gap locators all resolve
        // inside its dict before any later object — arbitrary binary JPEG bytes can never fool them (red-team).
        long baseOffset = inputPdf.Length;
        var appended = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        WriteObject(appended, offsets, baseOffset, sigNum, sigDict);
        if (appr is not null)
        {
            WriteImageObject(appended, offsets, baseOffset, imageNum, appr.ImageDictHead, appr.JpegBytes);
            WriteObject(appended, offsets, baseOffset, formNum, appr.FormDict);
        }
        WriteObject(appended, offsets, baseOffset, widgetNum, widgetDict);
        WriteObject(appended, offsets, baseOffset, acroFormNum, acroFormDict);
        WriteObject(appended, offsets, baseOffset, pageNum, pageUpdated);
        WriteObject(appended, offsets, baseOffset, catalogNum, catalogUpdated);

        long xrefStart = baseOffset + appended.Length;
        WriteXref(appended, offsets, newSize, catalogNum, model.PrevStartXref, xrefStart);

        byte[] full = Concat(inputPdf, appended.ToArray());

        // Locate the /Contents gap and the /ByteRange region in the FINAL bytes (offsets are now fixed).
        long sigObjOffset = offsets[sigNum];
        int contentsTokenPos = IndexOf(full, "/Contents", (int)sigObjOffset);
        int open = IndexOf(full, "<", contentsTokenPos);
        int close = IndexOf(full, ">", open + 1);
        int len1 = open + 1;             // bytes [0..open] inclusive (the '<' is the last signed byte of range 1)
        int start2 = close;              // the '>' is the first signed byte of range 2
        int len2 = full.Length - close;
        int[] byteRange = [0, len1, start2, len2];

        // Write real ByteRange into the reserved region (length invariant → no offset shift).
        int brBracket = IndexOf(full, "/ByteRange [", (int)sigObjOffset) + "/ByteRange [".Length;
        string brValue = $"0 {len1} {start2} {len2}";
        if (brValue.Length > ByteRangeReserved) throw new PdfSigningException("ByteRange overflowed its reserved width.");
        OverwriteAscii(full, brBracket, brValue.PadRight(ByteRangeReserved));

        byte[] digest = HashByteRange(full, byteRange);
        return new PdfSigningPreparation(full, open + 1, contentsHexLength, byteRange, digest);
    }

    /// <summary>Splices the detached CMS (hex) into the zero placeholder. Length-preserving → digest stays valid.</summary>
    public byte[] EmbedCms(PdfSigningPreparation prep, byte[] cmsDer)
    {
        ArgumentNullException.ThrowIfNull(prep);
        ArgumentNullException.ThrowIfNull(cmsDer);
        string hex = Convert.ToHexString(cmsDer);
        if (hex.Length > prep.ContentsHexLength)
            throw new PdfSigningException(
                $"CMS ({hex.Length} hex chars) exceeds the /Contents placeholder ({prep.ContentsHexLength}). Enlarge the placeholder.");

        byte[] outBytes = (byte[])prep.PreparedPdf.Clone();
        OverwriteAscii(outBytes, prep.ContentsGapStart, hex.PadRight(prep.ContentsHexLength, '0'));
        return outBytes;
    }

    private static byte[] HashByteRange(byte[] full, int[] br)
    {
        using var sha = SHA256.Create();
        sha.TransformBlock(full, br[0], br[1], null, 0);
        sha.TransformFinalBlock(full, br[2], br[3]);
        return sha.Hash!;
    }

    private static void WriteObject(MemoryStream s, Dictionary<int, long> offsets, long baseOffset, int num, string dict)
    {
        offsets[num] = baseOffset + s.Length;
        WriteAscii(s, $"{num} 0 obj\n{dict}\nendobj\n");
    }

    /// <summary>
    /// Writes an image XObject whose stream body is RAW binary (JPEG): the dict head + <c>stream\n</c> are ASCII,
    /// then the bytes are block-copied verbatim, then <c>\nendstream</c>. The trailing/leading EOLs around the body
    /// are framing only and are NOT counted in <c>/Length</c> (which equals the JPEG byte count exactly — H3).
    /// Never round-trips the JPEG through a string encoder.
    /// </summary>
    private static void WriteImageObject(
        MemoryStream s, Dictionary<int, long> offsets, long baseOffset, int num, string dictHead, byte[] binary)
    {
        offsets[num] = baseOffset + s.Length;
        WriteAscii(s, $"{num} 0 obj\n{dictHead}\nstream\n");
        s.Write(binary, 0, binary.Length);
        WriteAscii(s, "\nendstream\nendobj\n");
    }

    /// <summary>
    /// Validates the appearance JPEG + placement rect (fail-closed) and produces the image/form XObject pieces:
    /// the image dict head (Width/Height/ColorSpace derived from the JPEG SOF — single source of truth), the widget
    /// /Rect string, and the form XObject that draws the image into the rect. All PDF numbers use invariant
    /// formatting (red-team C1) so a vi-VN host never emits a comma decimal that corrupts the Rect/BBox/cm.
    /// </summary>
    private static AppearanceObjects BuildAppearance(
        PdfDocumentModel model, int pageNum, SignatureAppearance a, int imageNum)
    {
        if (a.JpegBytes is null or { Length: 0 })
            throw new PdfSigningException("Signature appearance JPEG is empty.");
        if (a.JpegBytes.Length > MaxAppearanceJpegBytes)
            throw new PdfSigningException($"Signature appearance JPEG exceeds {MaxAppearanceJpegBytes} bytes.");
        if (a.RectWidth <= 0 || a.RectHeight <= 0)
            throw new PdfSigningException("Signature appearance rectangle has a non-positive dimension.");

        var jpeg = JpegImageInfo.Parse(a.JpegBytes); // baseline-JPEG validation + W/H/ColorSpace (H1/H2/M1/L2)

        // Rect is in bottom-left PDF points; it must lie fully inside the page box (honours a non-zero origin — C2).
        var (llx, lly, urx, ury) = model.GetMediaBox(pageNum);
        double x0 = a.RectX, y0 = a.RectY, x1 = a.RectX + (double)a.RectWidth, y1 = a.RectY + (double)a.RectHeight;
        if (x0 < llx || y0 < lly || x1 > urx || y1 > ury)
            throw new PdfSigningException("Signature appearance rectangle falls outside the page MediaBox.");

        string rect = $"[{Num(x0)} {Num(y0)} {Num(x1)} {Num(y1)}]";
        string imageDictHead =
            $"<< /Type /XObject /Subtype /Image /Width {jpeg.Width} /Height {jpeg.Height} " +
            $"/ColorSpace {jpeg.ColorSpace} /BitsPerComponent 8 /Filter /DCTDecode /Length {a.JpegBytes.Length} >>";

        // Form XObject BBox == the rect's own extent; the unit image is scaled to fill it via the cm matrix. The
        // widget /Rect maps this form 1:1 onto the page, so the stamp fills exactly the chosen rectangle.
        string content = $"q {Num(a.RectWidth)} 0 0 {Num(a.RectHeight)} 0 0 cm /Im0 Do Q\n";
        int clen = Encoding.Latin1.GetByteCount(content);
        string formDict =
            $"<< /Type /XObject /Subtype /Form /FormType 1 /BBox [0 0 {Num(a.RectWidth)} {Num(a.RectHeight)}] " +
            $"/Resources << /XObject << /Im0 {imageNum} 0 R >> >> /Length {clen} >>\nstream\n{content}endstream";

        return new AppearanceObjects(rect, imageDictHead, a.JpegBytes, formDict);
    }

    /// <summary>Invariant, non-scientific PDF real formatting (banning the vi-VN comma decimal — red-team C1).</summary>
    private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed record AppearanceObjects(string Rect, string ImageDictHead, byte[] JpegBytes, string FormDict);

    private static void WriteXref(
        MemoryStream s, Dictionary<int, long> offsets, int size, int rootNum, long prev, long xrefStart)
    {
        var sb = new StringBuilder("xref\n");
        // Re-emit the free-list head (object 0) defensively. It is idempotent with the original xref and matches
        // what mainstream signers emit, reducing the odds a strict verifier rejects the incremental section.
        sb.Append("0 1\n0000000000 65535 f\r\n");
        foreach (var (start, nums) in ContiguousRuns(offsets.Keys))
        {
            sb.Append($"{start} {nums.Count}\n");
            foreach (int n in nums) sb.Append($"{offsets[n]:D10} 00000 n\r\n");
        }
        sb.Append($"trailer\n<< /Size {size} /Root {rootNum} 0 R /Prev {prev} >>\nstartxref\n{xrefStart}\n%%EOF\n");
        WriteAscii(s, sb.ToString());
    }

    private static IEnumerable<(int start, List<int> nums)> ContiguousRuns(IEnumerable<int> keys)
    {
        var sorted = keys.OrderBy(k => k).ToList();
        for (int i = 0; i < sorted.Count;)
        {
            int start = sorted[i];
            var run = new List<int> { start };
            int j = i + 1;
            while (j < sorted.Count && sorted[j] == sorted[j - 1] + 1) run.Add(sorted[j++]);
            yield return (start, run);
            i = j;
        }
    }

    private static string InjectBeforeDictEnd(string dict, string insertion)
    {
        int end = dict.LastIndexOf(">>", StringComparison.Ordinal);
        if (end < 0) throw new PdfSigningException("Object dictionary has no '>>' terminator.");
        return dict[..end] + insertion + dict[end..];
    }

    private static void WriteAscii(MemoryStream s, string text)
    {
        byte[] b = Encoding.Latin1.GetBytes(text);
        s.Write(b, 0, b.Length);
    }

    private static void OverwriteAscii(byte[] target, int at, string text)
    {
        byte[] b = Encoding.Latin1.GetBytes(text);
        Buffer.BlockCopy(b, 0, target, at, b.Length);
    }

    private static int IndexOf(byte[] haystack, string needle, int from)
    {
        byte[] n = Encoding.Latin1.GetBytes(needle);
        for (int i = from; i <= haystack.Length - n.Length; i++)
        {
            int k = 0;
            while (k < n.Length && haystack[i + k] == n[k]) k++;
            if (k == n.Length) return i;
        }
        throw new PdfSigningException($"Expected token '{needle}' not found from offset {from}.");
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        byte[] r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }
}

/// <summary>
/// The prepared (but not-yet-embedded) PDF: <see cref="PreparedPdf"/> carries the incremental update with a zero
/// <c>/Contents</c> placeholder and the final <c>/ByteRange</c>; <see cref="DocumentDigest"/> is SHA-256 over the
/// byte ranges and is what the signed attributes' messageDigest binds to.
/// </summary>
public sealed record PdfSigningPreparation(
    byte[] PreparedPdf,
    int ContentsGapStart,
    int ContentsHexLength,
    int[] ByteRange,
    byte[] DocumentDigest);
