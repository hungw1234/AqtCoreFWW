using System.Text;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Raw inputs for a visible signature stamp. The image bytes are an already-resolved PNG/JPEG (chosen by
/// <c>SignatureVisualType</c> at sign-time); the text fields are the values the stamp renders. Serial and
/// IssuerCa are server-derived from the Approved cert enrollment by the sign-time caller (P5) — never
/// client-supplied — so they are always present.
/// </summary>
public sealed record StampContent(
    byte[] SignatureImage,
    string SignerName,
    DateTimeOffset SignedAt,
    string? Reason,
    string Serial,
    string IssuerCa);

/// <summary>
/// The composed stamp ready for the PDF writer (P5) to draw: the pass-through image bytes (embedded as an
/// image XObject — no raster step, C2) plus the sanitized, overflow-bounded text lines drawn via the
/// content stream. Byte-level determinism is intentionally NOT a goal (M3).
/// </summary>
public sealed record ComposedStamp(
    byte[] SignatureImage,
    IReadOnlyList<string> TextLines);

/// <summary>
/// Composes a visible signature stamp's drawable primitives from <see cref="StampContent"/>: sanitizes each
/// text field (strips control chars/newlines, collapses whitespace, ellipsizes overflow — M4) and assembles
/// the display lines. Pure logic — no raster, no PDF graphics (the C2 pivot moved actual drawing to the P5
/// writer), so it lives in Application and is unit-tested directly.
/// </summary>
public interface ISignatureStampComposer
{
    ComposedStamp Compose(StampContent content);
}

/// <inheritdoc />
public sealed class SignatureStampComposer : ISignatureStampComposer
{
    /// <summary>Per-line character cap — a crafted 2000-char field is ellipsized, never overflowing the stamp.</summary>
    private const int MaxFieldLength = 80;

    public ComposedStamp Compose(StampContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = new List<string>
        {
            $"Ký bởi: {Sanitize(content.SignerName)}",
            $"Thời gian: {content.SignedAt:dd/MM/yyyy HH:mm}",
        };

        var reason = Sanitize(content.Reason);
        if (reason.Length > 0)
            lines.Add($"Lý do: {reason}");

        // Serial / Nhà CC are optional per signature profile — an empty value means the profile suppressed
        // that line, so it is skipped entirely (same convention as Reason) rather than rendering a bare label.
        var serial = Sanitize(content.Serial);
        if (serial.Length > 0)
            lines.Add($"Serial: {serial}");

        var issuer = Sanitize(content.IssuerCa);
        if (issuer.Length > 0)
            lines.Add($"Nhà CC: {issuer}");

        return new ComposedStamp(content.SignatureImage, lines);
    }

    /// <summary>
    /// Collapses every control char (incl. newline/tab) and whitespace run to a single space, trims, then
    /// ellipsizes past <see cref="MaxFieldLength"/>. Printable text — including Vietnamese diacritics and
    /// combining marks — is preserved verbatim. Defends the rendered stamp against injected newlines/control
    /// chars and oversize fields.
    /// </summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var ch in value)
        {
            if (char.IsControl(ch) || ch == ' ')
            {
                if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
                continue;
            }
            sb.Append(ch);
            lastWasSpace = false;
        }

        var cleaned = sb.ToString().Trim();
        return cleaned.Length <= MaxFieldLength
            ? cleaned
            : string.Concat(cleaned.AsSpan(0, MaxFieldLength - 1), "…");
    }
}
