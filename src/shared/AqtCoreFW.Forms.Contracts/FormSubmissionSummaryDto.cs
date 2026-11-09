namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module summary projection of a FormSubmission for BP-Run consumers (P3/P4/P6).
/// Lightweight — excludes full SubmissionData (use GetFormSubmissionQuery in Forms.Application
/// for the data dict when needed). Field types match domain (long FormTemplateId T1 internal PK;
/// Guid FormTemplatePublicId T2 external AK; long? WorkflowStateId T1 FK).
/// </summary>
public sealed record FormSubmissionSummaryDto(
    Guid Id,
    Guid TenantId,
    Guid FormTemplatePublicId,
    int FormTemplateVersion,
    Guid? WorkflowInstanceId,
    long? WorkflowStateId,
    DateTimeOffset SubmittedAt,
    Guid SubmittedBy,
    string Status,
    // Digital-signature verify-at-ingest evidence (null when the submission carried no signature).
    // SignerSubject is the full certificate subject DN; consumers extract the CN for display.
    bool? SignatureVerdict = null,
    string? SignerSubject = null,
    DateTimeOffset? SignatureVerifiedAt = null);
