using System.Text.RegularExpressions;
using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Signing;

/// <summary>
/// Best-effort extraction of a Vietnamese national id (CCCD/CMND) from a VGCA certificate subject DN,
/// plus organisation-name extraction and subject-type classification.
///
/// <para>Per QĐ-742, VGCA personal certificates carry the citizen id in the subject's
/// <c>serialNumber</c> RDN (OID 2.5.4.5), commonly prefixed — e.g.
/// <c>SERIALNUMBER=CCCD:012345678901</c>, <c>SERIALNUMBER=CMND:012345678</c>, or a bare number.
/// Some issuers place it in <c>UID</c> (OID 0.9.2342.19200300.100.1.1) instead. .NET renders the
/// DN with keyword RDNs (<c>SERIALNUMBER=</c>) or raw OIDs (<c>OID.2.5.4.5=</c>) depending on the
/// platform, so we probe both.</para>
///
/// <para><b>Best-effort by design.</b> This is the bootstrap binding path only — the forge-resistant
/// key is the enrolled serial+issuer/thumbprint. Subject-DN parsing is brittle (vendor formatting
/// varies, no spec guarantee), so a failure to extract returns null and the caller fails closed; it
/// never throws and never guesses.</para>
///
/// <para>Pure string utility (no infrastructure dependencies) — lives in Domain so both the
/// Infrastructure binding guard and the Application self-service enrollment handler can re-assert the
/// cert's national id against the user's <c>SigningNationalId</c> without crossing layers.</para>
/// </summary>
public static class VgcaCertSubjectParser
{
    // RDN type keywords that may carry the national id, in priority order.
    private static readonly string[] IdRdnKeys =
    [
        "SERIALNUMBER", "OID.2.5.4.5", "2.5.4.5",
        "UID", "OID.0.9.2342.19200300.100.1.1", "0.9.2342.19200300.100.1.1",
    ];

    // RDN type keywords that carry the organisation name (OID 2.5.4.10), in priority order.
    private static readonly string[] OrgRdnKeys =
    [
        "O", "OID.2.5.4.10", "2.5.4.10",
    ];

    // RDN keyword for the Common Name (OID 2.5.4.3).
    private const string CnKey = "CN";

    // A Vietnamese national id is EXACTLY 9 digits (legacy CMND) or 12 digits (CCCD) — never 10, 11 or 13.
    // The (?<!\d)…(?!\d) boundaries match an isolated run of exactly 9 or 12 digits, so an organisation
    // tax code (MST — 10 digits, or 13 with a branch suffix) carried in the same serialNumber/UID RDN is
    // NOT mistaken for a citizen id. This keeps a legitimate org cert classified as Organization (and thus
    // accepted on the USB enroll path) instead of being hard-rejected as a national-id mismatch.
    private static readonly Regex DigitRun = new(@"(?<!\d)(?:\d{12}|\d{9})(?!\d)", RegexOptions.Compiled);

    // Org tax-code (MST) prefixes — a value carrying one of these is an organisation identifier, never a
    // citizen id, regardless of digit length. Belt-and-suspenders alongside the strict 9/12 length rule.
    private static readonly string[] OrgTaxCodeMarkers = ["MST", "MSDN", "VATVN", "VNMST"];

    /// <summary>
    /// Extracts a 9–12 digit national id from the subject DN, stripping any prefix (CCCD:/CMND:/etc).
    /// Returns true + the digit string on success; false when no plausible id is present.
    /// </summary>
    public static bool TryExtractNationalId(string? subjectDn, out string nationalId)
    {
        nationalId = string.Empty;
        if (string.IsNullOrWhiteSpace(subjectDn))
            return false;

        // X500 DN components are comma-separated; the id value is digits (± a textual prefix),
        // so a naive comma split is safe here (no commas inside a digit/prefix value).
        foreach (var component in subjectDn.Split(','))
        {
            var trimmed = component.Trim();
            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;

            var key = trimmed[..eq].Trim().ToUpperInvariant();
            if (!IdRdnKeys.Contains(key)) continue;

            var value = trimmed[(eq + 1)..];

            // Skip an organisation tax code carried in this RDN — it is not a citizen id even if its digit
            // run were to satisfy the length rule (defends the rare 9-digit-MST edge alongside the strict regex).
            var upperValue = value.ToUpperInvariant();
            if (OrgTaxCodeMarkers.Any(marker => upperValue.Contains(marker)))
                continue;

            var match = DigitRun.Match(value);
            if (match.Success)
            {
                nationalId = match.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the organisation name from the subject DN.
    /// Reads the <c>O</c> RDN (OID 2.5.4.10); falls back to <c>CN</c> when absent.
    ///
    /// <para><b>Comma-escape caveat:</b> .NET's X500DistinguishedName renders commas <i>inside</i> an
    /// RDN value as <c>\,</c> (backslash-comma). A naive split on <c>,</c> would therefore break a
    /// multi-word org name like <c>O=Công ty ABC\, JSC</c> into two fragments. This implementation
    /// re-joins consecutive fragments whose left side ends with <c>\</c> (i.e. the escape character),
    /// then strips the escape to yield the correct value. Result is display-only — never a gate.</para>
    /// </summary>
    /// <param name="subjectDn">Subject distinguished name string (may be null or empty).</param>
    /// <param name="org">The extracted organisation name, or <see cref="string.Empty"/> on failure.</param>
    /// <returns>True when an <c>O</c> or <c>CN</c> value was found; false when subject is blank.</returns>
    public static bool TryExtractOrganizationName(string? subjectDn, out string org)
    {
        org = string.Empty;
        if (string.IsNullOrWhiteSpace(subjectDn))
            return false;

        // Re-join escaped commas: split on ',', then merge any fragment whose previous part
        // ends with '\' (the .NET DN escape for a literal comma inside an RDN value).
        var rawParts = subjectDn.Split(',');
        var components = new List<string>(rawParts.Length);
        var pending = string.Empty;
        foreach (var part in rawParts)
        {
            if (pending.Length > 0)
            {
                // Previous fragment ended with '\' — this fragment is a continuation of that value.
                pending = pending[..^1] + "," + part; // strip trailing '\', re-insert real comma
            }
            else
            {
                pending = part;
            }

            // If current accumulated value ends with '\', keep accumulating (more escaped commas ahead).
            if (pending.TrimEnd().EndsWith('\\'))
                continue;

            components.Add(pending);
            pending = string.Empty;
        }
        // Flush any trailing fragment that never closed (malformed DN — treat as-is).
        if (pending.Length > 0)
            components.Add(pending);

        string? cn = null;
        foreach (var component in components)
        {
            var trimmed = component.Trim();
            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;

            var key = trimmed[..eq].Trim().ToUpperInvariant();
            var value = trimmed[(eq + 1)..].Trim();

            if (OrgRdnKeys.Contains(key) && value.Length > 0)
            {
                org = value;
                return true;
            }

            if (key == CnKey && cn is null && value.Length > 0)
                cn = value;
        }

        // O not found — fall back to CN.
        if (cn is not null)
        {
            org = cn;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Classifies the certificate subject DN as Personal or Organisation and derives a display name.
    ///
    /// <para>Classification rule:
    /// <list type="bullet">
    ///   <item>If <see cref="TryExtractNationalId"/> succeeds ⇒ <see cref="CertSubjectType.Personal"/>;
    ///     display name = CN value (best-effort, empty when CN is absent).</item>
    ///   <item>Otherwise ⇒ <see cref="CertSubjectType.Organization"/>;
    ///     display name = O value, falling back to CN, falling back to empty string.</item>
    /// </list>
    /// </para>
    ///
    /// <para>Result is display/audit only. Never throws; returns <c>(Organization, "")</c> on empty input.</para>
    /// </summary>
    public static (CertSubjectType Type, string DisplayName) Classify(string? subjectDn)
    {
        if (TryExtractNationalId(subjectDn, out _))
        {
            // Personal: extract CN for the display name.
            var cn = ExtractCn(subjectDn);
            return (CertSubjectType.Personal, cn ?? string.Empty);
        }

        // Organisation: use O, falling back to CN, falling back to "".
        TryExtractOrganizationName(subjectDn, out var orgName);
        return (CertSubjectType.Organization, orgName);
    }

    /// <summary>Normalises a stored/free-text national id to its digit run for comparison.</summary>
    public static string NormalizeNationalId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var match = DigitRun.Match(value);
        return match.Success ? match.Value : value.Trim();
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the CN value using a naive comma split (safe: CN values in VN PKI certs don't contain commas).
    /// Returns null when CN is absent.
    /// </summary>
    private static string? ExtractCn(string? subjectDn)
    {
        if (string.IsNullOrWhiteSpace(subjectDn)) return null;

        foreach (var component in subjectDn.Split(','))
        {
            var trimmed = component.Trim();
            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;
            var key = trimmed[..eq].Trim().ToUpperInvariant();
            if (key == CnKey)
                return trimmed[(eq + 1)..].Trim();
        }

        return null;
    }
}
