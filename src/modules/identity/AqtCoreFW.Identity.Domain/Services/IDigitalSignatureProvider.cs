namespace AqtCoreFW.Identity.Domain.Services;

[Flags]
public enum SignatureProviderCapabilities
{
    None = 0,
    ServerSideSigning = 1,
    ClientSideSigning = 2,
    BatchSigning = 4,
    LongTermValidation = 8,
    OcspStapling = 16,
    RequiresLocalAgent = 32,  // FE SDK needs end-user desktop service install (e.g. VGCA SignService MSI)

    /// <summary>
    /// Provider can produce a raw RSA/ECDSA signature over a precomputed hash (Tier-1).
    /// Declared by: VGCA USB token (W1) + Viettel Mysign (W4).
    /// Payload = hash bytes; result Signature = raw sig bytes; certificate returned separately.
    /// </summary>
    RawSign = 64,

    /// <summary>
    /// Provider can produce an XMLDSig-signed XML document (Tier-2).
    /// Declared by: VGCA USB token for <c>vgca_sign_xml</c> operations (W2).
    /// Verification via <see cref="IStructuredSignatureService.VerifyXmlDsigAsync"/>.
    /// </summary>
    XmlDsig = 128,

    /// <summary>
    /// Provider can produce a JWS compact signature over JSON content (Tier-2).
    /// Declared by: VGCA USB token for <c>vgca_sign_json</c> operations (W2).
    /// Verification via <see cref="IStructuredSignatureService.VerifyJwsAsync"/>.
    /// </summary>
    JsonSign = 256,

    /// <summary>
    /// Provider supports Tier-3 doc-flow signing — VGCA SignService downloads PDF from BE,
    /// applies visible stamp (PIN + drag-drop), uploads signed PDF back.
    /// Declared by: VGCA USB token for doc-flow operations (W3 — <see cref="IDocumentSigningWorkflow"/>).
    /// BE never holds raw signature bytes for Tier-3 operations.
    /// </summary>
    DocFlow = 512,
}

public enum OcspMode { Soft, Hard }

public record CertificateInfo(
    string SerialNumber,
    string Subject,
    string Issuer,
    DateTime NotBefore,
    DateTime NotAfter,
    string Thumbprint,
    byte[] RawData);

public record ChainValidationResult(
    bool IsValid,
    string? FailureReason,
    IReadOnlyList<CertificateInfo> Chain,
    bool OcspChecked,
    bool OcspSucceeded);

/// <summary>
/// Request payload for <see cref="IDigitalSignatureProvider.SignAsync"/>.
///
/// <para><b>Tier mapping for new fields (D19):</b></para>
/// <list type="bullet">
///   <item><term><see cref="HashAlg"/></term>
///     <description>Tier-1 raw sign — hash algorithm applied to <see cref="Payload"/> before signing
///     (e.g. "SHA256", "SHA384"). Null = provider default (typically SHA256).
///     VGCA: maps to <c>hashAlg</c> in <c>vgca_sign_msg</c>.
///     Viettel: maps to <c>hashAlgorithm</c> in <c>/v1/signature/hash</c>.</description></item>
///   <item><term><see cref="Encode"/></term>
///     <description>Tier-1 raw sign — asymmetric algorithm to use (e.g. "RSA", "EC"). Null = provider default.
///     VGCA: maps to <c>encode</c> field. Viettel: maps to <c>signatureAlgorithm</c>.</description></item>
///   <item><term><see cref="RequestedFormat"/></term>
///     <description>Requested output format — <see cref="SignatureFormat.Raw"/> for Tier-1,
///     <see cref="SignatureFormat.XmlDsig"/> / <see cref="SignatureFormat.Jws"/> for Tier-2.
///     Null = provider selects its default format based on declared <see cref="SignatureProviderCapabilities"/>.</description></item>
/// </list>
///
/// <para><b>Backward compatibility:</b> existing 4-arg positional ctor callers continue to compile.
/// New fields have named defaults and must be specified by name.</para>
/// </summary>
public record SignRequest(
    byte[] Payload,
    string? UserId,
    string? Reason,
    IReadOnlyDictionary<string, string>? ExtraParameters,
    string? HashAlg = null,
    string? Encode = null,
    SignatureFormat? RequestedFormat = null);

/// <summary>
/// Result of a <see cref="IDigitalSignatureProvider.SignAsync"/> call.
/// When <c>IsPending=true</c> the provider accepted the request asynchronously — caller MUST poll
/// <c>v2/signatures/confirm</c> with <c>TransactionId</c>; do NOT inspect <c>Signature</c> (will be null).
///
/// <para><b>Viettel Mysign async modes (D19):</b> Viettel signHash API may return a status code
/// indicating the request is still in flight rather than a completed signature:
/// <list type="bullet">
///   <item><term>status = 4000</term><description>Request accepted, pending user confirmation on mobile app.
///     Set <c>IsPending=true</c>, populate <c>TransactionId</c>. Caller polls confirm endpoint.</description></item>
///   <item><term>status = 6000</term><description>Signing in progress (user confirmed, CA processing).
///     Set <c>IsPending=true</c>, populate <c>TransactionId</c>. Caller continues polling.</description></item>
///   <item><term>status = 2000</term><description>Completed — set <c>IsSuccess=true</c>, populate <c>Signature</c>.</description></item>
/// </list>
/// Both pending states share the same caller contract: do not treat as failure, do not inspect <c>Signature</c>.
/// </para>
/// </summary>
public record SignResult(
    bool IsSuccess,
    byte[]? Signature,
    CertificateInfo? SigningCertificate,
    string? FailureReason,
    string? TransactionId = null,
    bool IsPending = false);

/// <summary>
/// Request payload for <see cref="IDigitalSignatureProvider.VerifyAsync"/>.
///
/// <para><b>Additional fields for D19 tier routing:</b></para>
/// <list type="bullet">
///   <item><term><see cref="SignerCertDer"/></term>
///     <description>Tier-1 raw mode — DER-encoded signer certificate supplied explicitly, because raw
///     RSA/ECDSA signatures do not embed the certificate (unlike CMS/PKCS#7).
///     VGCA: base64-decoded <c>CertBase64</c> field from <c>vgca_sign_msg</c> response.
///     Null for Pkcs7/XmlDsig/Jws formats where the cert is embedded in the signature structure.</description></item>
///   <item><term><see cref="HashAlg"/></term>
///     <description>Hash algorithm used when generating the signature (default "SHA256").
///     Required to reconstruct the hash for Tier-1 raw verify.</description></item>
///   <item><term><see cref="Encode"/></term>
///     <description>Asymmetric algorithm used (default "RSA"). Required for Tier-1 raw verify.</description></item>
///   <item><term><see cref="Format"/></term>
///     <description>Binary layout of <see cref="Signature"/> bytes — drives the verification path.
///     Default <see cref="SignatureFormat.Pkcs7"/> preserves backward compatibility with all
///     existing callers that pass a CMS/PKCS#7 detached signature.</description></item>
/// </list>
///
/// <para><b>Backward compatibility:</b> existing 3-arg positional ctor callers continue to compile.
/// New fields have named defaults and must be specified by name.</para>
/// </summary>
public record VerifyRequest(
    byte[] Payload,
    byte[] Signature,
    OcspMode OcspMode,
    byte[]? SignerCertDer = null,
    string HashAlg = "SHA256",
    string Encode = "RSA",
    SignatureFormat Format = SignatureFormat.Pkcs7);

public record VerifyResult(
    bool IsValid,
    string? FailureReason,
    CertificateInfo? SigningCertificate,
    ChainValidationResult? ChainResult);

/// <summary>
/// Capability contract:
/// - <see cref="SignAsync"/> requires <see cref="SignatureProviderCapabilities.ServerSideSigning"/>; otherwise MUST throw <see cref="NotSupportedException"/>.
/// - <see cref="VerifyAsync"/> is unconditional — every provider MUST implement verify regardless of capabilities.
/// - For async server-side flows (Viettel Mysign sync/async-poll/async-callback modes), return <see cref="SignResult"/>
///   with <c>IsPending=true</c> + <c>TransactionId</c> populated; signature delivery happens via webhook callback
///   or follow-up confirm endpoint.
/// - Tier routing: check <see cref="Capabilities"/> bitmask before calling tier-specific methods.
///   Tier-1 (<see cref="SignatureProviderCapabilities.RawSign"/>): raw hash-based sign.
///   Tier-2 (<see cref="SignatureProviderCapabilities.XmlDsig"/> | <see cref="SignatureProviderCapabilities.JsonSign"/>):
///   structured document signing — verify via <see cref="IStructuredSignatureService"/>.
///   Tier-3 (<see cref="SignatureProviderCapabilities.DocFlow"/>): doc-flow via <see cref="IDocumentSigningWorkflow"/>.
/// </summary>
public interface IDigitalSignatureProvider
{
    string ProviderKey { get; }              // e.g. "usb-vgca", "remote-viettel-mysign"
    string DisplayName { get; }
    SignatureProviderCapabilities Capabilities { get; }

    Task<SignResult> SignAsync(SignRequest request, CancellationToken cancellationToken = default);
    Task<VerifyResult> VerifyAsync(VerifyRequest request, CancellationToken cancellationToken = default);
}
