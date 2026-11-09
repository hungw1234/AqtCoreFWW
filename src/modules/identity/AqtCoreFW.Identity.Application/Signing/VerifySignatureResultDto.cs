using AqtCoreFW.Identity.Domain.Services;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Result DTO returned by POST api/v1/digital-signature/verify.
/// IsValid=true only when both CMS/PKCS#7 signature is valid AND chain validation succeeds.
/// </summary>
public sealed record VerifySignatureResultDto(
    bool IsValid,
    string? FailureReason,
    CertificateInfo? SigningCertificate,
    ChainValidationResult? ChainResult);
