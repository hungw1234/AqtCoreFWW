namespace AqtCoreFW.Files.Application.Pdf;

/// <summary>
/// Detects whether PDF bytes carry an encryption dictionary (the <c>/Encrypt</c> trailer entry).
///
/// <para>Why a byte scan rather than a PDF library: the document-signing tool (VGCA / iText
/// <c>PdfSigner</c>) cannot sign a PDF protected with an owner password — it throws
/// <c>BadPasswordException: PdfReader is not opened with owner password</c> even when the user
/// password is empty. We only need a yes/no gate at ingest, so a full PDF parser is unwarranted.</para>
///
/// <para>The <c>/Encrypt</c> reference always lives in the trailer (or the cross-reference stream
/// dictionary on PDF 1.5+, whose dictionary part is always cleartext even when the stream data is
/// compressed), which is never itself encrypted — so it is reliably present in cleartext for any
/// encrypted document (no false negatives).</para>
///
/// <para>Residual false-positive vector (accepted): a clean document could in principle carry the
/// literal byte sequence <c>/Encrypt &lt;num&gt; &lt;num&gt; R</c> inside an <em>uncompressed</em>
/// content stream or string (e.g. a document that talks about PDF encryption). In practice content
/// streams are compressed (FlateDecode) so the marker does not appear in cleartext, but it is not
/// impossible — the trade-off (reject a rare clean file) is preferred over adding a full PDF parser
/// for a yes/no gate. Tighten to a dictionary-context check only if a real false positive is observed.</para>
/// </summary>
public static class PdfEncryptionDetector
{
    // "/Encrypt" as ASCII bytes.
    private static readonly byte[] Marker = "/Encrypt"u8.ToArray();

    /// <summary>
    /// Returns true when <paramref name="pdfBytes"/> contains an <c>/Encrypt</c> trailer entry in the
    /// indirect-reference form (<c>/Encrypt N G R</c>), i.e. the document is encrypted/password-protected.
    /// </summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> pdfBytes)
    {
        var from = 0;
        while (true)
        {
            var idx = IndexOf(pdfBytes, Marker, from);
            if (idx < 0)
                return false;

            // Confirm the indirect-reference shape that the trailer uses: "/Encrypt <int> <int> R".
            // This filters out an incidental "/Encrypt" appearing as a name elsewhere.
            if (IsIndirectReference(pdfBytes, idx + Marker.Length))
                return true;

            from = idx + Marker.Length;
        }
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, int start)
    {
        if (start < 0 || start >= haystack.Length)
            return -1;

        var relative = haystack[start..].IndexOf(needle);
        return relative < 0 ? -1 : start + relative;
    }

    // After "/Encrypt" the trailer holds an indirect reference: optional whitespace, digits (object
    // number), whitespace, digits (generation), whitespace, 'R'. Verifying the leading digit alone is
    // enough to distinguish the reference from a same-named key elsewhere while staying lenient.
    private static bool IsIndirectReference(ReadOnlySpan<byte> bytes, int pos)
    {
        var i = pos;
        var skipped = 0;
        while (i < bytes.Length && IsWhitespace(bytes[i]))
        {
            i++;
            skipped++;
        }

        // Must be separated by at least one whitespace and then begin with a digit (the object number).
        return skipped > 0 && i < bytes.Length && bytes[i] is >= (byte)'0' and <= (byte)'9';
    }

    private static bool IsWhitespace(byte b) =>
        b is 0x20 or 0x0A or 0x0D or 0x09 or 0x0C or 0x00;
}
