using System.Text.Json.Serialization;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

// ── Login (POST /vtss/service/ras/v1/login) ──────────────────────────────────

public sealed record MysignLoginRequest(
    [property: JsonPropertyName("client_id")]     string ClientId,
    [property: JsonPropertyName("client_secret")] string ClientSecret,
    [property: JsonPropertyName("profile_id")]    string ProfileId,
    [property: JsonPropertyName("user_id")]       string UserId);

public sealed record MysignLoginResponse(
    [property: JsonPropertyName("access_token")]  string? AccessToken,
    [property: JsonPropertyName("token_type")]    string? TokenType,
    // Viettel UAT returns expires_in as a JSON number (e.g. 3393). The deserializer is configured with
    // NumberHandling.AllowReadingFromString so a stringified "3600" is also accepted defensively.
    [property: JsonPropertyName("expires_in")]    int? ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    // Application-level error fields (200 OK with error body). Error codes can arrive as a bare number.
    [property: JsonPropertyName("error"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription);

// ── CertificateInfo (POST /vtss/service/certificates/info) ───────────────────

public sealed record MysignCertInfoRequest(
    [property: JsonPropertyName("client_id")]  string ClientId,
    [property: JsonPropertyName("profile_id")] string ProfileId,
    [property: JsonPropertyName("user_id")]    string UserId,
    // Required by the cert-info endpoint to return the certificate chain. Omitting it yields HTTP 400
    // error 58100 ("invalid/wrong-format request"). Value "chain" per Mysign API SDK v1.9 §1.2.2.
    [property: JsonPropertyName("certificates")] string Certificates = "chain");

/// <summary>
/// Single item in the certificate info array returned by Viettel Mysign.
/// Field names follow the spec; <c>SerialNumber</c> is PascalCase per Viettel spec (non-standard).
/// </summary>
public sealed record MysignCertInfoItem(
    [property: JsonPropertyName("description")]   string? Description,
    [property: JsonPropertyName("credential_id")] string? CredentialId,
    [property: JsonPropertyName("cert")]          MysignCertDetail? Cert,
    // Application-level error. Error codes can arrive as a bare number.
    [property: JsonPropertyName("error"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription);

public sealed record MysignCertDetail(
    [property: JsonPropertyName("status")]       string? Status,
    // certificates[0] = DER-encoded signer cert in base64; additional = intermediates
    [property: JsonPropertyName("certificates")] List<string>? Certificates,
    [property: JsonPropertyName("issuerDN")]     string? IssuerDN,
    // Note: Viettel spec uses PascalCase "SerialNumber" (not snake_case)
    [property: JsonPropertyName("SerialNumber")] string? SerialNumber,
    [property: JsonPropertyName("subjectDN")]    string? SubjectDN,
    // GeneralizedTime format: "YYYYMMDDHHMMSSZ"
    [property: JsonPropertyName("validFrom")]    string? ValidFrom,
    [property: JsonPropertyName("validTo")]      string? ValidTo);

// ── SignHash (POST /vtss/service/signHash) ────────────────────────────────────

/// <summary>
/// One document descriptor in a signHash request. The signature is bound to the corresponding
/// <c>hash</c> by array position; <c>document_name</c> is shown on the signer's Mysign mobile app.
/// </summary>
public sealed record MysignSignDocument(
    [property: JsonPropertyName("document_id")]   int DocumentId,
    // Base64(UTF-8) of a display name — see MysignDocumentName for the charset/length rules Viettel enforces.
    [property: JsonPropertyName("document_name")] string DocumentName);

// The signHash body is a distinct schema from login/cert-info (Mysign API SDK v1.9 §1.2.3 / §2.2.3):
// camelCase algo keys, a mandatory client_secret/numSignatures/documents set, and PascalCase credentialID.
// Sending the login-style snake_case shape yields HTTP 400 error 58100 ("invalid/wrong-format request").
public sealed record MysignSignHashRequest(
    // Signer certificate id from CertInfo. Viettel keys this PascalCase "credentialID".
    [property: JsonPropertyName("credentialID")]  string CredentialId,
    [property: JsonPropertyName("client_id")]     string ClientId,
    // signHash requires client_secret IN THE BODY (unlike cert-info, which the Bearer token alone authenticates).
    [property: JsonPropertyName("client_secret")] string ClientSecret,
    // Number of signatures requested == number of documents/hashes.
    [property: JsonPropertyName("numSignatures")] int NumSignatures,
    [property: JsonPropertyName("documents")]     List<MysignSignDocument> Documents,
    // Base64-encoded hash(es) — one element per document, same order. Must be a JSON array.
    [property: JsonPropertyName("hash")]          List<string> Hash,
    // SHA-256 OID: 2.16.840.1.101.3.4.2.1
    [property: JsonPropertyName("hashAlgo")]      string HashAlgo,
    // RSA encryption OID: 1.2.840.113549.1.1.1
    [property: JsonPropertyName("signAlgo")]      string SignAlgo,
    // 0 = sync (blocks until user confirms); 2 = async-poll; 3 = async-callback.
    [property: JsonPropertyName("async")]         int Async,
    // Callback URL Mysign POSTs the result to (async=3 only). Null for async 0/2 → dropped by
    // WhenWritingNull so the wire body is byte-identical to the shipped sync/poll shape.
    // [UNVERIFIED] exact key name ("callbackUrl" vs "callback_url" vs "notifyUrl") must be confirmed
    // against the Viettel Mysign SDK doc before merge; the body-assert test pins whatever is chosen here.
    [property: JsonPropertyName("callbackUrl")]   string? CallbackUrl = null);

public sealed record MysignSignHashResponse(
    // Success (sync async=0): [ base64-encoded raw RSA signature ]
    [property: JsonPropertyName("signatures")]        List<string>? Signatures,
    // Async=2: transactionId returned immediately; signatures populated only on status=1 from /requests/status
    [property: JsonPropertyName("transactionId")]     string? TransactionId,
    // Status polling (/requests/status) — e.g. "1" success, "4000" waiting, "4002" rejected.
    // Pure-integer codes; Viettel may serialize them as a bare number → flexible string read.
    [property: JsonPropertyName("status"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Status,
    // Application-level error (200 OK with error body) — OR 400 response. Error codes can arrive as a bare number.
    [property: JsonPropertyName("error"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription);
