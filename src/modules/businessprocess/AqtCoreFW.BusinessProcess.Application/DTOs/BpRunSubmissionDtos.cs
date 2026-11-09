namespace AqtCoreFW.BusinessProcess.Application.DTOs;

/// <summary>
/// One submitted field: the snapshot field type + label (vi/en) paired with the value the user entered.
/// Type is the FormFieldType name (e.g. "Text", "File", "Signature") so the client can render values
/// type-aware (image for signatures, download link for files) instead of dumping raw text.
/// </summary>
public sealed record BpRunSubmissionFieldDto(
    string Key,
    string Type,
    string LabelVi,
    string LabelEn,
    object? Value);

/// <summary>
/// Read-only projection of the latest submission for one form inside a BP-Run.
/// WorkflowStateId null = intake form; non-null = the state form bound to that state.
/// Fields follow the snapshot DisplayOrder (active fields only — inactive fields are not snapshotted).
///
/// FormSubmission.Status is intentionally NOT projected: it is the Forms-module standalone approval
/// lifecycle (Pending→Approved/Rejected via the forms review screen). Inside a BP-Run the lifecycle
/// is driven by the workflow state, so the submission status stays "Pending" forever and would be
/// misleading noise here — the authoritative status is the run's current state.
/// </summary>
public sealed record BpRunSubmissionDto(
    Guid FormTemplatePublicId,
    int FormTemplateVersion,
    long? WorkflowStateId,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<BpRunSubmissionFieldDto> Fields,
    // Submission-level digital-signature evidence (QĐ-742 verify-at-ingest). IsSigned true only when
    // the proof verified at ingest; SignerName is the certificate CN (full DN/IDs kept server-side).
    bool IsSigned = false,
    string? SignerName = null,
    DateTimeOffset? SignatureVerifiedAt = null);
