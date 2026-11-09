using System.Globalization;
using System.Text;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Builds the <c>document_name</c> value for a Mysign signHash request.
///
/// Viettel constrains this field hard: the Base64-decoded text may contain ONLY
/// [A-Za-z0-9], space, underscore and hyphen, and the Base64 STRING must be under
/// 100 characters. Violating either yields HTTP 400 error 58100 ("invalid/wrong-format
/// request") — the same failure class as omitting a mandatory field.
///
/// The signer-supplied reason is free text (often Vietnamese with diacritics), so it is
/// NOT safe to Base64 verbatim. This helper decomposes accents, drops every disallowed
/// character, truncates to keep the encoding under the length ceiling, and falls back to a
/// constant when nothing usable remains — guaranteeing a spec-valid value for any input.
///
/// document_name is display-only (shown on the signer's mobile app); it is NOT bound into
/// the signed hash, so lossy sanitization does not affect signature integrity.
/// </summary>
internal static class MysignDocumentName
{
    private const string Fallback = "Document";

    // Base64 expands ~4/3; cap the source so the encoded string stays comfortably below 100 chars.
    private const int MaxSourceChars = 72;

    public static string Encode(string? reason)
    {
        var source = string.IsNullOrWhiteSpace(reason) ? Fallback : reason;

        // NFD decomposes accented vowels into base+combining mark, but the Vietnamese letter "đ"/"Đ"
        // (U+0111/U+0110) has NO decomposition — it would be dropped as disallowed, silently deleting
        // every "đ" word. Fold it to d/D first so names like "Sửa đổi" render as "Sua doi", not "Sua oi".
        var folded = source.Replace('đ', 'd').Replace('Đ', 'D');

        var decomposed = folded.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            // Drop the combining accent marks left behind by NFD decomposition (e.g. "Ký" -> "Ky").
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
                or ' ' or '_' or '-')
            {
                sb.Append(ch);
            }
        }

        var cleaned = sb.ToString().Trim();
        if (cleaned.Length == 0)
            cleaned = Fallback;
        if (cleaned.Length > MaxSourceChars)
            cleaned = cleaned[..MaxSourceChars].Trim();

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(cleaned));
    }
}
