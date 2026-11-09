using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>Thrown when an input PDF cannot be safely prepared for an incremental-update signature.</summary>
public sealed class PdfSigningException(string message) : Exception(message);

/// <summary>
/// A minimal, <b>fail-closed</b> reader of the structures an incremental-update signature needs: the previous
/// <c>startxref</c>, the classic xref offset map, <c>/Size</c>, the catalog (<c>/Root</c>), the first page, and the
/// raw dict text of those objects so the writer can re-emit them with the added signature keys.
///
/// <para><b>Hardening stance (S1).</b> The uploaded-PDF path is untrusted, so every read is bounded and anything
/// this spike does not explicitly support — xref STREAMS (PDF 1.5+), encrypted docs, an existing AcroForm, an
/// indirect <c>/Annots</c>, a missing Root/Pages/Page — throws <see cref="PdfSigningException"/> rather than
/// guessing. A mis-parse must fail closed (no signature) instead of producing a structurally-wrong PDF.</para>
/// </summary>
public sealed partial class PdfDocumentModel
{
    private readonly byte[] _bytes;
    private readonly Dictionary<int, long> _offsets = new();

    public long PrevStartXref { get; private set; }
    public int Size { get; private set; }
    public int RootObjectNumber { get; private set; }

    private PdfDocumentModel(byte[] bytes) => _bytes = bytes;

    public static PdfDocumentModel Parse(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (pdf.Length < 32 || !Latin1(pdf, 0, 5).StartsWith("%PDF-", StringComparison.Ordinal))
            throw new PdfSigningException("Input is not a PDF (missing %PDF- header).");

        var model = new PdfDocumentModel(pdf);
        model.ParseXrefChain();
        return model;
    }

    /// <summary>Latin-1 decode of a bounded byte window (PDF structure is ASCII/Latin-1; never decode the whole file).</summary>
    private static string Latin1(byte[] b, long start, int len)
    {
        if (start < 0 || start + len > b.Length) throw new PdfSigningException("Bounded read past end of file.");
        return Encoding.Latin1.GetString(b, (int)start, len);
    }

    private void ParseXrefChain()
    {
        PrevStartXref = FindLastStartXref();
        long xrefOffset = PrevStartXref;
        var seen = new HashSet<long>();

        // Follow the /Prev chain so an already-incrementally-updated PDF still yields a complete offset map. Bounded
        // by `seen` to reject a cyclic /Prev (a crafted-PDF DoS vector).
        while (xrefOffset >= 0 && seen.Add(xrefOffset))
        {
            var (trailerSize, root, prev) = ParseOneXrefSection(xrefOffset);
            if (Size == 0 && trailerSize > 0) Size = trailerSize;
            if (RootObjectNumber == 0 && root > 0) RootObjectNumber = root;
            xrefOffset = prev;
            if (seen.Count > 64) throw new PdfSigningException("Too many xref sections (possible crafted /Prev chain).");
        }

        if (RootObjectNumber == 0) throw new PdfSigningException("Trailer has no /Root.");
        if (Size == 0) throw new PdfSigningException("Trailer has no /Size.");
    }

    private long FindLastStartXref()
    {
        int window = Math.Min(2048, _bytes.Length);
        string tail = Latin1(_bytes, _bytes.Length - window, window);
        var m = StartXrefRegex().Matches(tail);
        if (m.Count == 0) throw new PdfSigningException("No startxref found.");
        return long.Parse(m[^1].Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private (int size, int root, long prev) ParseOneXrefSection(long offset)
    {
        // A classic xref section starts with the literal `xref` keyword. An xref STREAM (`N G obj <</Type/XRef`)
        // is explicitly out of spike scope → fail closed.
        string head = Latin1(_bytes, offset, Math.Min(16, _bytes.Length - (int)offset));
        if (!head.TrimStart().StartsWith("xref", StringComparison.Ordinal))
            throw new PdfSigningException("Cross-reference streams (PDF 1.5+) are not supported by the spike writer.");

        int pos = (int)offset + head.IndexOf("xref", StringComparison.Ordinal) + 4;
        pos = SkipEol(pos);

        // Subsections: "<start> <count>" then <count> 20-byte entries, until the `trailer` keyword.
        while (true)
        {
            string line = Latin1(_bytes, pos, Math.Min(40, _bytes.Length - pos)).TrimStart();
            if (line.StartsWith("trailer", StringComparison.Ordinal)) break;

            var hdr = SubsectionHeaderRegex().Match(line);
            if (!hdr.Success) throw new PdfSigningException("Malformed xref subsection header.");
            int startObj = int.Parse(hdr.Groups[1].Value, CultureInfo.InvariantCulture);
            int count = int.Parse(hdr.Groups[2].Value, CultureInfo.InvariantCulture);
            if (count is < 0 or > 1_000_000) throw new PdfSigningException("Implausible xref subsection count.");

            // Advance past the header line to the first 20-byte entry.
            pos += Latin1(_bytes, pos, Math.Min(40, _bytes.Length - pos)).IndexOf(hdr.Value, StringComparison.Ordinal) + hdr.Length;
            pos = SkipEol(pos);

            for (int i = 0; i < count; i++)
            {
                string entry = Latin1(_bytes, pos, 20);
                // "nnnnnnnnnn ggggg n" — record only in-use ('n') entries; never overwrite a newer revision.
                if (entry.Length >= 18 && entry[17] == 'n')
                {
                    long off = long.Parse(entry[..10], CultureInfo.InvariantCulture);
                    _offsets.TryAdd(startObj + i, off);
                }
                pos += 20;
            }
        }

        // Parse the trailer dict (bounded read of the dict text following `trailer`).
        int trailerPos = pos + Latin1(_bytes, pos, Math.Min(40, _bytes.Length - pos)).IndexOf("trailer", StringComparison.Ordinal);
        string trailerDict = ExtractDict(trailerPos + "trailer".Length);
        if (trailerDict.Contains("/Encrypt", StringComparison.Ordinal))
            throw new PdfSigningException("Encrypted PDFs are not supported by the spike writer.");

        int size = ReadIntKey(trailerDict, "Size");
        int root = ReadRefKeyObjNum(trailerDict, "Root");
        long prev = TryReadIntKey(trailerDict, "Prev", out int p) ? p : -1;
        return (size, root, prev);
    }

    /// <summary>Returns the raw <c>&lt;&lt; ... &gt;&gt;</c> dict text of an indirect object (balanced-scan, bounded).</summary>
    public string GetObjectDict(int objNumber)
    {
        if (!_offsets.TryGetValue(objNumber, out long off))
            throw new PdfSigningException($"Object {objNumber} not in xref map.");
        string head = Latin1(_bytes, off, Math.Min(32, _bytes.Length - (int)off));
        var m = ObjHeaderRegex().Match(head);
        if (!m.Success) throw new PdfSigningException($"Object {objNumber} header malformed at its xref offset.");
        return ExtractDict(off + m.Index + m.Length);
    }

    /// <summary>Extracts a balanced <c>&lt;&lt; ... &gt;&gt;</c> starting at/after <paramref name="from"/>.</summary>
    private string ExtractDict(long from)
    {
        int i = (int)from;
        while (i < _bytes.Length && _bytes[i] != (byte)'<') i++;
        if (i + 1 >= _bytes.Length || _bytes[i + 1] != (byte)'<')
            throw new PdfSigningException("Expected a dictionary '<<'.");
        int depth = 0, start = i;
        for (; i < _bytes.Length - 1; i++)
        {
            if (_bytes[i] == '<' && _bytes[i + 1] == '<') { depth++; i++; }
            else if (_bytes[i] == '>' && _bytes[i + 1] == '>')
            {
                depth--; i++;
                if (depth == 0) return Latin1(_bytes, start, i - start + 1);
            }
            if (i - start > 1_000_000) throw new PdfSigningException("Dictionary exceeds bounded size.");
        }
        throw new PdfSigningException("Unterminated dictionary.");
    }

    public int FirstPageObjectNumber()
    {
        string catalog = GetObjectDict(RootObjectNumber);
        if (catalog.Contains("/AcroForm", StringComparison.Ordinal))
            throw new PdfSigningException("PDF already has an AcroForm; spike writer does not merge existing forms.");
        int pagesObj = ReadRefKeyObjNum(catalog, "Pages");
        string pages = GetObjectDict(pagesObj);
        // Inheritable attributes: an /Annots on the page-tree node would be silently dropped by the page rewrite,
        // so a node carrying inherited annotations is rejected rather than signed with annotations lost.
        if (pages.Contains("/Annots", StringComparison.Ordinal))
            throw new PdfSigningException("Page-tree node carries inherited /Annots; spike writer does not preserve them.");
        var kid = KidsFirstRefRegex().Match(pages);
        if (!kid.Success) throw new PdfSigningException("/Pages has no resolvable /Kids first entry.");
        int pageNum = int.Parse(kid.Groups[1].Value, CultureInfo.InvariantCulture);

        // The first kid MUST be a leaf /Type /Page. A nested page tree (/Kids[0] = an intermediate /Pages node)
        // would mis-structure the signed PDF; the spike fails closed instead of signing against a non-page node.
        string firstKid = GetObjectDict(pageNum);
        if (!LeafPageRegex().IsMatch(firstKid))
            throw new PdfSigningException("First /Kids entry is not a leaf /Type /Page (nested page trees unsupported).");
        return pageNum;
    }

    /// <summary>
    /// Resolves the object number of the 1-based <paramref name="pageNumber"/>th page of a <b>flat</b> page tree
    /// (<c>/Pages → /Kids = [ leaf-page refs ]</c>). Page 1 delegates to <see cref="FirstPageObjectNumber"/> so the
    /// single-page path (and its golden-bytes output) is byte-identical. For page &gt; 1 it reuses the same
    /// fail-closed guards (no <c>/AcroForm</c>, no inherited <c>/Annots</c>) and additionally rejects an
    /// out-of-range index or a selected kid that is not a leaf <c>/Type /Page</c> — a nested page tree means the Nth
    /// <c>/Kids</c> entry is not the Nth page, so signing it would mis-place the stamp. Fail closed, never guess.
    /// </summary>
    public int PageObjectNumber(int pageNumber)
    {
        if (pageNumber < 1) throw new PdfSigningException("Page number must be >= 1.");
        if (pageNumber == 1) return FirstPageObjectNumber();

        string catalog = GetObjectDict(RootObjectNumber);
        if (catalog.Contains("/AcroForm", StringComparison.Ordinal))
            throw new PdfSigningException("PDF already has an AcroForm; spike writer does not merge existing forms.");
        int pagesObj = ReadRefKeyObjNum(catalog, "Pages");
        string pages = GetObjectDict(pagesObj);
        if (pages.Contains("/Annots", StringComparison.Ordinal))
            throw new PdfSigningException("Page-tree node carries inherited /Annots; spike writer does not preserve them.");

        var kidsArray = KidsArrayRegex().Match(pages);
        if (!kidsArray.Success) throw new PdfSigningException("/Pages has no resolvable /Kids array.");
        var refs = KidRefRegex().Matches(kidsArray.Groups[1].Value);
        if (pageNumber > refs.Count)
            throw new PdfSigningException($"Requested page {pageNumber} but the flat page tree has {refs.Count} kid(s).");

        int pageNum = int.Parse(refs[pageNumber - 1].Groups[1].Value, CultureInfo.InvariantCulture);
        string kid = GetObjectDict(pageNum);
        if (!LeafPageRegex().IsMatch(kid))
            throw new PdfSigningException("Requested page is not a leaf /Type /Page (nested page trees unsupported).");
        return pageNum;
    }

    public int CatalogObjectNumber => RootObjectNumber;

    /// <summary>
    /// Resolves the first page's effective <c>/MediaBox</c> as <c>[llx lly urx ury]</c> in PDF points, honouring
    /// inheritance (a MediaBox declared on the <c>/Pages</c> node, not the leaf) and a non-zero origin. Fails
    /// closed if neither the page leaf nor its <c>/Pages</c> parent carries a well-formed 4-real MediaBox — the
    /// stamp-placement validation needs a trustworthy page box, never a guessed one (red-team C2).
    /// </summary>
    public (double Llx, double Lly, double Urx, double Ury) GetMediaBox(int pageNumber)
    {
        if (TryReadMediaBox(GetObjectDict(pageNumber), out var box)) return box;

        // Inherited: walk to the /Pages node referenced by the catalog (the common place MediaBox is hoisted).
        string catalog = GetObjectDict(RootObjectNumber);
        int pagesObj = ReadRefKeyObjNum(catalog, "Pages");
        if (TryReadMediaBox(GetObjectDict(pagesObj), out box)) return box;

        throw new PdfSigningException("No /MediaBox on the first page or its /Pages parent (cannot place stamp).");
    }

    private static bool TryReadMediaBox(string dict, out (double Llx, double Lly, double Urx, double Ury) box)
    {
        var m = MediaBoxRegex().Match(dict);
        if (!m.Success) { box = default; return false; }
        double P(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
        double llx = P(1), lly = P(2), urx = P(3), ury = P(4);
        if (urx <= llx || ury <= lly) throw new PdfSigningException("Degenerate /MediaBox (non-positive extent).");
        box = (llx, lly, urx, ury);
        return true;
    }

    private int SkipEol(int pos)
    {
        while (pos < _bytes.Length && (_bytes[pos] == '\r' || _bytes[pos] == '\n' || _bytes[pos] == ' ')) pos++;
        return pos;
    }

    private static int ReadIntKey(string dict, string key)
        => TryReadIntKey(dict, key, out int v) ? v : throw new PdfSigningException($"Trailer key /{key} missing.");

    private static bool TryReadIntKey(string dict, string key, out int value)
    {
        var m = Regex.Match(dict, $@"/{key}\s+(\d+)(?!\s+\d+\s+R)");
        value = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return m.Success;
    }

    private static int ReadRefKeyObjNum(string dict, string key)
    {
        var m = Regex.Match(dict, $@"/{key}\s+(\d+)\s+\d+\s+R");
        if (!m.Success) throw new PdfSigningException($"Key /{key} is not an indirect reference.");
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"startxref\s+(\d+)")] private static partial Regex StartXrefRegex();
    [GeneratedRegex(@"^(\d+)\s+(\d+)")] private static partial Regex SubsectionHeaderRegex();
    [GeneratedRegex(@"^(\d+)\s+(\d+)\s+obj")] private static partial Regex ObjHeaderRegex();
    [GeneratedRegex(@"/Kids\s*\[\s*(\d+)\s+\d+\s+R")] private static partial Regex KidsFirstRefRegex();
    [GeneratedRegex(@"/Kids\s*\[(.*?)\]", RegexOptions.Singleline)] private static partial Regex KidsArrayRegex();
    [GeneratedRegex(@"(\d+)\s+\d+\s+R")] private static partial Regex KidRefRegex();
    [GeneratedRegex(@"/Type\s*/Page(?![a-zA-Z])")] private static partial Regex LeafPageRegex();
    [GeneratedRegex(@"/MediaBox\s*\[\s*(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s*\]")]
    private static partial Regex MediaBoxRegex();
}
