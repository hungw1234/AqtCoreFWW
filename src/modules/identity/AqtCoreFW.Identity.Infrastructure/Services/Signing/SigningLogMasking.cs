namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Masking helpers for signing diagnostic logs.
///
/// The remote-signing user identifier is the signer's CCCD (12-digit national ID,
/// Confidential PII under QĐ 742 §4.4.a). Logging it in plaintext leaks PII into
/// diagnostic output. Callers MUST pass identifiers through <see cref="MaskUserId"/>
/// before logging so only a non-identifying tail survives.
/// </summary>
internal static class SigningLogMasking
{
    /// <summary>
    /// Masks a signing user identifier for log output, keeping only the last 4 chars
    /// (e.g. CCCD "079201001234" → "***1234"). Identifiers of 4 chars or fewer are
    /// masked entirely ("****") so a short value can never expose itself; null/empty
    /// becomes "(none)" so the log still distinguishes "missing" from "masked".
    /// </summary>
    public static string MaskUserId(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return "(none)";

        return userId.Length <= 4
            ? "****"
            : "***" + userId[^4..];
    }
}
