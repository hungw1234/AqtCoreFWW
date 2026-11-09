namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>
/// Read-side projection of a single per-role signer evidence row.
/// Returned as part of FormSubmissionSignaturesDto for multi-signer submissions.
/// </summary>
public sealed record FormSubmissionSignerEvidenceDto(
    long Id,
    string SignerRoleKey,
    bool SignatureVerdict,
    string? SignerSubject,
    string? SignerSerial,
    DateTimeOffset SignatureVerifiedAt,
    Guid? SignerUserId,
    DateTimeOffset SignedAt);

/// <summary>
/// Aggregates all per-role signer evidence rows for a submission.
/// Lightweight read model for exposing multi-signer status — SignerEvidences is empty
/// for single-signer submissions (evidence is on FormSubmission scalar columns).
/// </summary>
public sealed record FormSubmissionSignaturesDto(
    Guid SubmissionId,
    IReadOnlyList<FormSubmissionSignerEvidenceDto> SignerEvidences);
