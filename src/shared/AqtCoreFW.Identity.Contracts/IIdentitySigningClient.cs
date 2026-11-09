namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module digital-signature verification facade for callers outside the Identity module
/// (e.g. Forms verify-at-ingest). Kept separate from <see cref="IIdentityModuleClient"/> so the
/// signing concern can be extracted to its own NuGet/service boundary independently.
/// Monolith: InProcessIdentitySigningClient (resolves tenant config + provider, runs local PKIX verify).
/// Microservice: gRPC client when Identity is extracted.
///
/// The contract surface uses only primitives + the DTOs below — no Identity.Domain types leak across
/// the boundary, so consumers stay free of an Identity.Domain reference (module-isolation invariant).
/// </summary>
public interface IIdentitySigningClient
{
    /// <summary>
    /// Verify a digital signature over <see cref="SignatureVerifyRequest.Payload"/>.
    /// A missing/unregistered provider or an unknown format is reported as
    /// <see cref="SignatureVerifyResult.IsValid"/>=false (NOT an exception) so callers can hard-block
    /// uniformly on any verify failure.
    /// </summary>
    Task<SignatureVerifyResult> VerifySignatureAsync(SignatureVerifyRequest request, CancellationToken ct = default);
}

/// <summary>
/// Verify request. Class-based init-only (mirrors the other Identity.Contracts DTOs).
/// <para><see cref="Format"/> crosses the boundary as a string ("Raw"/"Pkcs7"/"XmlDsig"/"Jws") so the
/// Identity.Domain <c>SignatureFormat</c> enum does not appear on the contract surface.</para>
/// </summary>
public sealed class SignatureVerifyRequest
{
    /// <summary>Bytes that were signed (content for Pkcs7; the precomputed-hash input for Raw).</summary>
    public byte[] Payload { get; init; } = [];

    /// <summary>Raw signature bytes.</summary>
    public byte[] Signature { get; init; } = [];

    /// <summary>DER-encoded signer certificate — required for Raw format (cert not embedded). Null for Pkcs7.</summary>
    public byte[]? SignerCertDer { get; init; }

    /// <summary>"Raw" | "Pkcs7" | "XmlDsig" | "Jws". Unknown value → verify fails (no enum-parse crash).</summary>
    public string Format { get; init; } = "Raw";

    /// <summary>Hash algorithm used when signing (Raw verify needs it to reconstruct the hash).</summary>
    public string HashAlg { get; init; } = "SHA256";

    /// <summary>Asymmetric algorithm used ("RSA"/"EC"). Required for Raw verify.</summary>
    public string Encode { get; init; } = "RSA";

    /// <summary>
    /// Provider key ("usb-vgca"/"remote-viettel-mysign"). When null the impl falls back to the
    /// tenant's configured provider. Callers should pass the same provider that drove signing to
    /// avoid a provider-mismatch verify failure.
    /// </summary>
    public string? ProviderKey { get; init; }

    /// <summary>Tenant whose <c>TenantSignatureConfig</c> supplies the OCSP mode (and fallback provider).</summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// The assigned signer this signature must belong to (anti-impersonation). When set, a successful
    /// cryptographic verify is additionally checked against the signer's enrolled certificate (or
    /// CCCD-subject fallback); a mismatch makes the whole verify fail (<see cref="SignatureVerifyResult.IsValid"/>=false).
    /// Null on paths with no assigned signer (e.g. anonymous public-form submission) → binding skipped.
    /// </summary>
    public Guid? SignerUserId { get; init; }
}

/// <summary>
/// Verify result with audit-grade evidence. <see cref="ChainEvidenceJson"/> is a deterministic
/// serialization of the validated chain + OCSP outcome (stable property order via fixed DTO);
/// <see cref="SignerCertDer"/> carries the signer certificate bytes for offline re-verification.
/// </summary>
public sealed class SignatureVerifyResult
{
    public bool IsValid { get; init; }
    public string? FailureReason { get; init; }
    public string? SignerSubject { get; init; }
    public string? SignerSerial { get; init; }
    public byte[]? SignerCertDer { get; init; }
    public string? ChainEvidenceJson { get; init; }
    public DateTimeOffset VerifiedAt { get; init; }
}

/// <summary>
/// Fixed-shape DTO serialized into <see cref="SignatureVerifyResult.ChainEvidenceJson"/>.
/// Declared-property order is stable across builds → deterministic JSON for offline audit diffing.
/// </summary>
public sealed class ChainEvidenceDto
{
    public bool IsValid { get; init; }
    public string? FailureReason { get; init; }
    public bool OcspChecked { get; init; }
    public bool OcspSucceeded { get; init; }
    public List<ChainCertEvidenceDto> Chain { get; init; } = [];
}

/// <summary>One certificate in the validated chain. Carries DER (base64) for full offline re-verification.</summary>
public sealed class ChainCertEvidenceDto
{
    public string SerialNumber { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public DateTime NotBefore { get; init; }
    public DateTime NotAfter { get; init; }
    public string Thumbprint { get; init; } = string.Empty;
    public string RawDataBase64 { get; init; } = string.Empty;
}
