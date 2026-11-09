using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Services;

/// <summary>
/// Signing mode for a DigitalSignature field.
/// The mode is stored as <c>{"signatureMode":"CanonicalJson"}</c> or
/// <c>{"signatureMode":"ServerDocHash"}</c> inside <c>FormField.ValidationRulesJson</c>.
///
/// <para><b>Default when absent or unparseable:</b> <see cref="CanonicalJson"/> (Mode-1).
/// This preserves behaviour for all fields created before Mode-2 was introduced.</para>
///
/// <para><b>CanonicalJson (Mode-1):</b> the signed payload is the UTF-8 canonical JSON of
/// the covered scalar fields (ordinal-sorted dict). The FE builds and signs these bytes
/// directly using the Mode-1 spec.</para>
///
/// <para><b>ServerDocHash (Mode-2):</b> the signed payload is a 32-byte SHA-256 of a
/// server-constructed document projection wrapper. The FE fetches the hash from the
/// <c>POST …/signature-doc-hash</c> endpoint and passes it as the signing payload.</para>
/// </summary>
public enum SignatureMode
{
    /// <summary>Mode-1: sign the canonical JSON of covered scalar fields.</summary>
    CanonicalJson,

    /// <summary>Mode-2: sign the server-computed SHA-256 document hash.</summary>
    ServerDocHash
}

/// <summary>
/// Parses <see cref="SignatureMode"/> from <c>FormField.ValidationRulesJson</c>.
/// Any parse failure returns <see cref="SignatureMode.CanonicalJson"/> (safe default).
/// </summary>
public static class SignatureModeParser
{
    private static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parse the signing mode from a ValidationRulesJson string.
    /// Returns <see cref="SignatureMode.CanonicalJson"/> when the input is null, empty, or
    /// does not contain a recognizable <c>signatureMode</c> key.
    /// </summary>
    public static SignatureMode Parse(string? validationRulesJson)
    {
        if (string.IsNullOrWhiteSpace(validationRulesJson))
            return SignatureMode.CanonicalJson;

        try
        {
            using var doc = JsonDocument.Parse(validationRulesJson);
            // Guard ValueKind explicitly: TryGetProperty throws InvalidOperationException on a
            // non-object root, and GetString throws on a non-string value — neither is a
            // JsonException, so an admin mistyping signatureMode (e.g. a number) would otherwise
            // 500 every submit. Any shape mismatch falls through to the safe CanonicalJson default.
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("signatureMode", out var prop)
                && prop.ValueKind == JsonValueKind.String
                && string.Equals(prop.GetString(), "ServerDocHash", StringComparison.OrdinalIgnoreCase))
            {
                return SignatureMode.ServerDocHash;
            }
        }
        catch (JsonException)
        {
            // Malformed JSON — fall through to default
        }

        return SignatureMode.CanonicalJson;
    }
}
