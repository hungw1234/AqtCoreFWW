namespace AqtCoreFW.SharedKernel.Compliance;

/// <summary>
/// Call-site masking for Confidential PII (QĐ 742 §66) that must never reach a log sink in full.
///
/// <para>
/// Email addresses and phone numbers are encrypted at rest by <c>EncryptedStringConverter</c>, so
/// logging either in full would place plaintext PII outside that encryption boundary. The Serilog
/// secret-masking policy cannot catch this: it matches property NAMES against a secret-word pattern
/// (which "To"/"Email"/"PhoneNumber" do not hit) and only inspects complex objects — a string passed
/// as a message-template argument never reaches it. Masking therefore has to happen at the call site.
/// </para>
///
/// <para>
/// Both helpers keep just enough of the value for an operator to triage a delivery failure without
/// writing the value itself: an email keeps its full domain (tenant-level triage), a phone keeps its
/// last four digits (complaint correlation).
/// </para>
/// </summary>
public static class PiiLogMasking
{
    /// <summary>
    /// Masks the local part of an address, keeping the domain so operators can triage a delivery
    /// failure by tenant domain (e.g. <c>user@gov.vn</c> → <c>u***@gov.vn</c>). Null/blank →
    /// <c>(none)</c>; an unparseable value (no <c>@</c>, or a leading <c>@</c>) is masked whole so no
    /// local-part character leaks.
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "(none)";

        var atIndex = email.IndexOf('@');

        // No '@' — not a parseable address; mask the whole string rather than leak it.
        if (atIndex <= 0)
            return new string('*', email.Length);

        var localPart = email.AsSpan(0, atIndex);
        var domain = email.AsSpan(atIndex); // includes the '@'

        // Keep the first local-part character as a triage hint, star the rest.
        return localPart.Length == 1
            ? string.Concat("*", domain)
            : string.Concat(localPart[..1], new string('*', localPart.Length - 1), domain);
    }

    /// <summary>
    /// Keeps only the last four digits of a phone number, enough to correlate a delivery with a
    /// complaint without writing the number itself to a log sink. Null/blank → <c>(none)</c>.
    /// </summary>
    public static string MaskPhone(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return "(none)";

        return phoneNumber.Length <= 4
            ? new string('*', phoneNumber.Length)
            : string.Concat(new string('*', phoneNumber.Length - 4), phoneNumber.AsSpan(phoneNumber.Length - 4));
    }
}
