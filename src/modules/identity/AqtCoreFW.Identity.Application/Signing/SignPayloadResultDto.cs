using AqtCoreFW.Identity.Domain.Services;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Result DTO returned by POST api/v1/digital-signature/sign.
/// IsPending=true when the remote provider accepted the request asynchronously (VNPT SmartCA PENDING flow).
/// Caller MUST poll v2/signatures/confirm with TransactionId when IsPending=true — Signature will be null.
/// </summary>
public sealed record SignPayloadResultDto(
    bool IsSuccess,
    bool IsPending,
    byte[]? Signature,
    string? TransactionId,
    string? FailureReason,
    CertificateInfo? SigningCertificate);
