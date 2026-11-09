using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Services;

/// <summary>
/// Deterministic canonical byte producer for digital-signature verify-at-ingest.
/// Supports two modes:
///
/// <list type="bullet">
///   <item><b>Mode-1 (CanonicalJson):</b> signs the raw form data as a sorted JSON dict.
///   <c>Payload = CanonicalizeMode1(data, activeFields)</c> — provider hashes Payload once.</item>
///   <item><b>Mode-2 (ServerDocHash):</b> signs a 32-byte SHA-256 of a server-constructed
///   document projection. <c>Payload = ComputeMode2DocHash(template, data, activeFields)</c>.
///   The provider then SHA-256's Payload (hash-of-hash) — self-consistent because both sign-time
///   and ingest-time compute the same Payload = docHash bytes.</item>
/// </list>
///
/// <para><b>WHY two methods share a single field-selection helper:</b>
/// Mode-1 and Mode-2 both cover the same scalar fields (same covered-field rule).
/// <c>BuildCanonicalDataObject</c> is the single source of truth for field selection + value
/// coercion; diverging the two sets would create false-rejects at ingest time.</para>
///
/// <para><b>Covered-field rule (scalar-only v1):</b>
/// A key is included if and only if:
/// <list type="number">
///   <item>It is present as a key in <c>data</c>, AND</item>
///   <item>There is an ACTIVE <see cref="FormField"/> in <c>activeFields</c> whose
///         <see cref="FormField.FieldKey"/> matches (ordinal), AND</item>
///   <item>That field's <see cref="FormField.Type"/> is one of the scalar data field types:
///         Text, Number, Date, Textarea, Boolean, RichText, EnumRef, InternalRef, ExternalRef.</item>
/// </list>
/// Excluded types (never signed):
/// <list type="bullet">
///   <item>UI-only: Section, Label, Divider — no data content.</item>
///   <item>Formula — server-computed; not user-entered data.</item>
///   <item>File — binary content, not a form-field scalar value.</item>
///   <item>Signature (PNG drawing, type 6) — binary artifact.</item>
///   <item>DigitalSignature — the field that declares the signing requirement itself.</item>
///   <item>Complex multi-value: TableField, AddressField, DateRange — non-scalar shapes.</item>
/// </list>
/// </para>
///
/// <para><b>Null handling:</b> if a covered key's value is null, the key is OMITTED entirely
/// from the canonical dict (not represented as a JSON null).</para>
///
/// <para><b>String coercion:</b>
/// <list type="bullet">
///   <item><see cref="JsonElement"/> String → <c>GetString()</c></item>
///   <item><see cref="JsonElement"/> Number | True | False | other → <c>GetRawText()</c></item>
///   <item>Boxed <see cref="bool"/> → "true" / "false" (invariant lowercase)</item>
///   <item><see cref="string"/> → verbatim</item>
///   <item>Anything else → <c>Convert.ToString(value, CultureInfo.InvariantCulture)</c></item>
/// </list>
/// </para>
///
/// <para><b>Unicode normalization:</b> every string is NFC-normalized before inclusion.
/// This is the same normalization the FE applies with <c>str.normalize("NFC")</c>.</para>
///
/// <para><b>Key ordering:</b> keys are sorted with <see cref="StringComparer.Ordinal"/>
/// (UTF-16 code-unit order). JavaScript's <c>Object.keys().sort()</c> produces the same
/// order for ASCII-only field keys.</para>
///
/// <para><b>Serialization:</b> compact JSON, UTF-8 encoded, with
/// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> so non-ASCII characters in values
/// are NOT percent-escaped — they appear as literal UTF-8 bytes.</para>
/// </summary>
public static class FormDataCanonicalizer
{
    /// <summary>
    /// Scalar data field types included in the canonical payload.
    /// This set is the "signed surface" — changes here break all existing signatures (semver-breaking).
    /// </summary>
    private static readonly HashSet<FormFieldType> ScalarFieldTypes =
    [
        FormFieldType.Text,
        FormFieldType.Number,
        FormFieldType.Date,
        FormFieldType.Textarea,
        FormFieldType.Boolean,
        FormFieldType.RichText,
        FormFieldType.EnumRef,
        FormFieldType.InternalRef,
        FormFieldType.ExternalRef
    ];

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        // UnsafeRelaxedJsonEscaping: Vietnamese characters (e.g. "Hà Nội") appear as literal
        // UTF-8 bytes, NOT \uXXXX sequences — matches browser JSON.stringify behaviour.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Produce the canonical UTF-8 bytes that were signed (or should be signed) for a Mode-1
    /// CanonicalJson digital-signature field.
    ///
    /// Output is a UTF-8 compact JSON of an ordinal-sorted dictionary mapping covered field keys
    /// to their NFC-normalized string representation. Null-valued covered keys are omitted.
    /// </summary>
    /// <param name="data">Raw submission data dict from the request (values may be JsonElement or CLR types).</param>
    /// <param name="activeFields">All ACTIVE FormField entities from the template.</param>
    public static byte[] CanonicalizeMode1(
        IDictionary<string, object?> data,
        IEnumerable<FormField> activeFields)
    {
        var canonical = BuildCanonicalDataObject(data, activeFields);
        var json = JsonSerializer.Serialize(canonical, SerializeOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Compute the 32-byte SHA-256 document hash for Mode-2 (ServerDocHash) signatures.
    ///
    /// The signed artifact is:
    /// <c>SHA256( UTF8( JSON( { "template": code, "version": version, "data": canonicalDataObject } ) ) )</c>
    ///
    /// The wrapper binds the signature to template identity + version + covered data.
    /// Both sign-time (FE fetches this hash) and ingest-time (BE re-hashes same data) call this
    /// method — determinism is guaranteed because the wrapped projection is a pure function of
    /// (code, version, data, activeFields) with no timestamps or random elements.
    ///
    /// The returned 32 bytes become <c>SignatureVerifyRequest.Payload</c>. The provider then
    /// applies SHA-256 to Payload (hash-of-hash), which is self-consistent: sign-time and
    /// ingest-time both produce the same Payload = docHash bytes.
    /// </summary>
    /// <param name="templateCode">FormTemplate.Code — stable slug binding signature to template identity.</param>
    /// <param name="templateVersion">FormTemplate.Version at time of hash computation.</param>
    /// <param name="data">Raw submission data dict.</param>
    /// <param name="activeFields">All ACTIVE FormField entities from the template.</param>
    /// <returns>32-byte SHA-256 of the Mode-2 wrapper bytes.</returns>
    public static byte[] ComputeMode2DocHash(
        string templateCode,
        int templateVersion,
        IDictionary<string, object?> data,
        IEnumerable<FormField> activeFields)
    {
        var wrapperBytes = BuildMode2WrapperBytes(templateCode, templateVersion, data, activeFields);
        return SHA256.HashData(wrapperBytes);
    }

    /// <summary>
    /// Build the canonical data object (ordinal-sorted dict of covered scalar fields).
    /// This is shared between Mode-1 serialization and Mode-2 wrapper construction so that
    /// the two modes cover EXACTLY the same field set — a divergence would produce false-rejects.
    /// </summary>
    internal static SortedDictionary<string, string> BuildCanonicalDataObject(
        IDictionary<string, object?> data,
        IEnumerable<FormField> activeFields)
    {
        // Build a lookup from FieldKey → FormField for active scalar fields only
        var scalarFields = activeFields
            .Where(f => f.IsActive && ScalarFieldTypes.Contains(f.Type))
            .ToDictionary(f => f.FieldKey, StringComparer.Ordinal);

        // SortedDictionary with Ordinal comparator guarantees deterministic key order
        var canonical = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var kvp in data)
        {
            // Only include keys that map to a covered active scalar field
            if (!scalarFields.ContainsKey(kvp.Key))
                continue;

            // Null values are omitted from the canonical set
            if (kvp.Value is null)
                continue;

            var coerced = CoerceToString(kvp.Value);
            if (coerced is null)
                continue;

            // NFC normalize: ensures ă, ơ, ư etc. produced by different input methods
            // (decomposed vs precomposed) hash identically.
            canonical[kvp.Key] = coerced.Normalize(NormalizationForm.FormC);
        }

        return canonical;
    }

    /// <summary>
    /// Build the Mode-2 wrapper bytes:
    /// UTF-8( JSON( { "template": code, "version": version, "data": canonicalDataObject } ) )
    /// using UnsafeRelaxedJsonEscaping and compact (no-indent) serialization.
    /// </summary>
    private static byte[] BuildMode2WrapperBytes(
        string templateCode,
        int templateVersion,
        IDictionary<string, object?> data,
        IEnumerable<FormField> activeFields)
    {
        var canonicalData = BuildCanonicalDataObject(data, activeFields);

        // Build wrapper as a plain object. Property order is fixed: template → version → data.
        // Use anonymous type serialized via System.Text.Json — key order matches declaration order.
        var wrapper = new
        {
            template = templateCode,
            version  = templateVersion,
            data     = canonicalData
        };

        var json = JsonSerializer.Serialize(wrapper, SerializeOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Coerce a raw submission value to its canonical string representation.
    /// Returns null only when the input is null (caller must omit null from the dict).
    /// </summary>
    private static string? CoerceToString(object? value)
    {
        if (value is null)
            return null;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                // String: use GetString() which returns the decoded string (no JSON quotes)
                JsonValueKind.String => element.GetString(),
                // Number, True, False, and any other kind: use raw JSON text ("1.5", "true", "false")
                _ => element.GetRawText()
            };
        }

        // Boxed bool: use invariant lowercase strings (matches JS `String(true)` === "true")
        if (value is bool b)
            return b ? "true" : "false";

        // Already a CLR string: use verbatim (normalize is applied by caller)
        if (value is string s)
            return s;

        // Anything else (decimal, long, double, etc.): invariant culture ToString
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
