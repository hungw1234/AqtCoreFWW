using System.Text;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing.Fixtures;

/// <summary>
/// Builds deterministic PDFs with a classic cross-reference table (no xref streams) and byte-exact offsets — the
/// controlled "sample input PDF" Phase 2 signs, plus anomaly variants that the writer must reject (fail closed).
/// </summary>
public static class MinimalPdfFactory
{
    /// <summary>A single-page, classic-xref PDF with no AcroForm and no page /Annots — the happy path.</summary>
    public static byte[] CreateSinglePage(string text = "AqtCoreFW PAdES Spike")
    {
        string streamContent = $"BT /F1 24 Tf 72 700 Td ({Escape(text)}) Tj ET\n";
        int streamLen = Encoding.Latin1.GetByteCount(streamContent);
        return Build(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {streamLen} >>\nstream\n{streamContent}endstream",
        ]);
    }

    /// <summary>
    /// A two-page, flat page-tree PDF (/Pages /Kids [3 0 R 4 0 R]) — page 1 = obj 3, page 2 = obj 4. Proves the
    /// writer can target the Nth page of a flat tree for a visible stamp.
    /// </summary>
    public static byte[] CreateTwoPage()
    {
        string s1 = "BT /F1 24 Tf 72 700 Td (Page 1) Tj ET\n";
        string s2 = "BT /F1 24 Tf 72 700 Td (Page 2) Tj ET\n";
        int l1 = Encoding.Latin1.GetByteCount(s1);
        int l2 = Encoding.Latin1.GetByteCount(s2);
        return Build(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                "/Resources << /Font << /F1 7 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                "/Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R >>",
            $"<< /Length {l1} >>\nstream\n{s1}endstream",
            $"<< /Length {l2} >>\nstream\n{s2}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ]);
    }

    /// <summary>Single page with a caller-supplied MediaBox (e.g. a non-zero origin) — proves stamp-placement
    /// bounds honour llx/lly, not a hardcoded 0.</summary>
    public static byte[] CreateSinglePageWithMediaBox(string mediaBox) => Build(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} /Resources << >> /Contents 4 0 R >>",
        "<< /Length 0 >>\nstream\n\nendstream",
    ]);

    /// <summary>Page-tree node carries inherited <c>/Annots</c> — the writer must fail closed (would drop them).</summary>
    public static byte[] CreateWithInheritedAnnots() => Build(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 /Annots [] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
    ]);

    /// <summary>Nested page tree: the catalog's /Pages first kid is another /Pages node, not a leaf /Page.</summary>
    public static byte[] CreateNestedPageTree() => Build(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",                      // top node → first kid is obj 4
        "<< /Type /Page /Parent 4 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
        "<< /Type /Pages /Parent 2 0 R /Kids [3 0 R] /Count 1 >>",       // obj 4: nested intermediate node
    ]);

    /// <summary>Generic builder: objects are numbered 1..N (root = object 1); emits a byte-exact classic xref.</summary>
    private static byte[] Build(string[] objects)
    {
        var sb = new StringBuilder();
        var offsets = new long[objects.Length + 1]; // 1-based object numbers
        sb.Append("%PDF-1.7\n%âãÏÓ\n");             // binary marker comment (Latin-1 high bytes)

        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i + 1] = Encoding.Latin1.GetByteCount(sb.ToString());
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        long xrefOffset = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append("xref\n");
        sb.Append($"0 {objects.Length + 1}\n");
        sb.Append("0000000000 65535 f\r\n");
        for (int i = 1; i <= objects.Length; i++)
            sb.Append($"{offsets[i]:D10} 00000 n\r\n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
