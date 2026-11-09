using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// FormSubmission — immutable record of a user's form submission.
/// SubmissionData: JSON string (Json mode only) or null (Table mode — data in dynamic table).
/// FormTemplateVersion: snapshot of the template version at submit time.
/// Status: Pending → Approved | Rejected (approval workflow).
///
/// T2 entity (AMEND-A3): AuditableEntity + IPublicIdentified.
///   PublicId uses NewIdRandom() (v4) — prevents timestamp-leak of submission timing (PDPL).
///   FormTemplateId: long (FK to T1 FormTemplate).
///   WorkflowInstanceId: Guid? — references WorkflowInstance.PublicId (T2 AK), stays Guid.
///   ConsentRecordId: Guid? — external ref, stays Guid.
/// </summary>
public sealed class FormSubmission : AuditableEntity, IPublicIdentified, ITenantScoped
{
    public Guid PublicId { get; private set; }
    public Guid TenantId { get; private set; }
    public long FormTemplateId { get; private set; }
    public int FormTemplateVersion { get; private set; }

    /// <summary>
    /// JSON data for Json-mode submissions. Null for Table-mode (data lives in dynamic table).
    /// May contain PII fields marked IsPii in the form schema — handle as Confidential.
    /// </summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public string? SubmissionData { get; private set; }

    [DataClassification(DataClassificationLevel.Internal)]
    public Guid SubmittedBy { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }

    // RTBF: allow writes to SubmittedBy (Guid.Empty for guest submissions)
    public bool IsAnonymized { get; private set; }

    // --- PDPL Consent ---
    public bool ConsentGiven { get; private set; }
    /// <summary>External ref to consent record — stays Guid (external system boundary).</summary>
    public Guid? ConsentRecordId { get; private set; }

    // --- Workflow integration ---
    /// <summary>
    /// Workflow instance tracking this submission's approval or BP Run context. Null = manual approval / standalone.
    /// References WorkflowInstance.PublicId (T2 AK) — stays Guid.
    /// </summary>
    public Guid? WorkflowInstanceId { get; private set; }

    /// <summary>
    /// Workflow state where this submission was captured (BP Run state-form path). Null = intake or manual.
    /// References WorkflowState.Id (T1 long PK).
    /// </summary>
    public long? WorkflowStateId { get; private set; }

    // --- Approval workflow ---
    public SubmissionStatus Status { get; private set; } = SubmissionStatus.Pending;
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewComment { get; private set; }

    // --- Digital signature evidence — single-signer path (verify-at-ingest, QĐ-742 integrity) ---
    // All nullable: a no-signature submission (template without a DigitalSignature field) leaves these null.
    // For multi-signer templates (template.SignerRoles.Count > 1) per-role evidence goes to the
    // SignerEvidences child collection via RecordSignerEvidence; these scalar fields stay null for
    // those submissions so the single-signer and multi-signer read paths remain non-overlapping.
    /// <summary>Outcome of verify-at-ingest. Null = no signature on this submission; true = verified valid.</summary>
    public bool? SignatureVerdict { get; private set; }
    /// <summary>Signer certificate subject DN captured at verify time.</summary>
    public string? SignerSubject { get; private set; }
    /// <summary>Signer certificate serial number.</summary>
    public string? SignerSerial { get; private set; }
    /// <summary>Server UTC instant the signature was verified.</summary>
    public DateTimeOffset? SignatureVerifiedAt { get; private set; }
    /// <summary>Deterministic JSON of the validated chain + OCSP outcome (audit-grade re-verify evidence).</summary>
    [DataClassification(DataClassificationLevel.Internal)]
    public string? SignatureChainEvidence { get; private set; }
    /// <summary>Signer certificate DER bytes — kept for offline re-verification (public cert, not secret).</summary>
    public byte[]? SignerCertDer { get; private set; }

    // --- Multi-signer evidence collection ---
    // Populated when template.SignerRoles.Count > 1. Each role that completes signing
    // appends one row here. The single-signer scalar block above (SignatureVerdict …
    // SignerCertDer / RecordSignatureEvidence) is the single-signer path and remains
    // exactly as-is; multi-signer evidence goes here instead.
    private readonly List<FormSubmissionSignerEvidence> _signerEvidences = [];

    public IReadOnlyCollection<FormSubmissionSignerEvidence> SignerEvidences => _signerEvidences.AsReadOnly();

    /// <summary>
    /// Append a per-role signer evidence row for multi-signer templates.
    /// Write-once per role — caller guards against duplicating the same role key.
    /// For single-signer templates use <see cref="RecordSignatureEvidence"/> instead.
    /// </summary>
    public void RecordSignerEvidence(FormSubmissionSignerEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        _signerEvidences.Add(evidence);
    }

    private FormSubmission() { } // EF Core

    /// <summary>
    /// RTBF bypass — explicitly permitted exception to the immutability invariant.
    /// Replaces SubmissionData JSON with anonymized version (IsPii fields replaced with [DA AN DANH]).
    /// Only callable by FormsPiiAnonymizer in the RTBF pipeline.
    /// </summary>
    public void AnonymizeSubmissionData(string anonymizedJson)
    {
        SubmissionData = anonymizedJson;
        IsAnonymized = true;
    }

    /// <summary>Approve a Pending submission. Throws if not Pending.</summary>
    public void Approve(Guid reviewedBy, string? comment)
    {
        if (Status != SubmissionStatus.Pending)
            throw new InvalidOperationException(
                $"GOV_FRM_017: Chỉ có thể duyệt submission ở trạng thái Pending (hiện tại: {Status}).");

        Status = SubmissionStatus.Approved;
        ReviewedBy = reviewedBy;
        ReviewedAt = DateTimeOffset.UtcNow;
        ReviewComment = comment;
    }

    /// <summary>Reject a Pending submission. Comment is required for audit trail.</summary>
    public void Reject(Guid reviewedBy, string comment)
    {
        if (Status != SubmissionStatus.Pending)
            throw new InvalidOperationException(
                $"GOV_FRM_017: Chỉ có thể từ chối submission ở trạng thái Pending (hiện tại: {Status}).");

        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("GOV_FRM_018: Lý do từ chối không được để trống.", nameof(comment));

        Status = SubmissionStatus.Rejected;
        ReviewedBy = reviewedBy;
        ReviewedAt = DateTimeOffset.UtcNow;
        ReviewComment = comment;
    }

    /// <summary>Link PDPL consent record to this submission.</summary>
    public void LinkConsent(Guid consentRecordId)
    {
        ConsentRecordId = consentRecordId;
        ConsentGiven = true;
    }

    /// <summary>Link workflow instance for automated approval tracking.</summary>
    public void LinkWorkflow(Guid workflowInstanceId)
    {
        WorkflowInstanceId = workflowInstanceId;
    }

    // --- PDF signing correlation ---
    /// <summary>
    /// Correlates this submission to its latest USB-token signing request (Identity module).
    /// Null = no signing request initiated; non-null = a DocFlow signing request was initiated
    /// for this submission's server-generated PDF.
    /// The signed PDF storage key is NOT stored here — it is read via the signing request projection
    /// (pull-on-poll) to avoid a cross-module write from Identity back into Forms on callback completion.
    /// </summary>
    public Guid? SigningRequestId { get; private set; }

    /// <summary>
    /// Records the signing request correlation. At-most-once: a second call throws, preventing
    /// accidental overwrites during concurrent sign initiations on the same submission.
    /// To re-sign, a new submission or a deliberate re-link after a supervisor review is required.
    /// </summary>
    public void LinkSigningRequest(Guid requestId)
    {
        if (SigningRequestId.HasValue)
            throw new InvalidOperationException(
                $"GOV_FRM_030: FormSubmission {PublicId} đã liên kết với signing request {SigningRequestId}. Không thể ghi đè.");
        SigningRequestId = requestId;
    }

    /// <summary>
    /// Write-once signature evidence — SINGLE-SIGNER PATH ONLY.
    /// Part of the submission's fact-of-record, captured at create-time (verify-at-ingest) BEFORE
    /// first persist — a documented, narrow exception to immutability, like
    /// <see cref="LinkConsent"/> / <see cref="LinkWorkflow"/>. Not intended for post-persist mutation.
    /// For multi-signer templates (template.SignerRoles.Count &gt; 1) use
    /// <see cref="RecordSignerEvidence"/> to append rows to <see cref="SignerEvidences"/> instead.
    /// </summary>
    public void RecordSignatureEvidence(
        bool verdict,
        string? signerSubject,
        string? signerSerial,
        DateTimeOffset verifiedAt,
        string? chainEvidenceJson,
        byte[]? signerCertDer)
    {
        SignatureVerdict = verdict;
        SignerSubject = signerSubject;
        SignerSerial = signerSerial;
        SignatureVerifiedAt = verifiedAt;
        SignatureChainEvidence = chainEvidenceJson;
        SignerCertDer = signerCertDer;
    }

    /// <summary>
    /// Factory method — no update methods; submissions are immutable records of fact.
    /// PublicId uses NewIdRandom() (v4) to prevent timestamp-leak of submission timing (PDPL).
    /// </summary>
    public static FormSubmission Create(
        IGuidGenerator gen,
        Guid tenantId,
        long formTemplateId,
        int formTemplateVersion,
        string? submissionData,
        Guid submittedBy,
        bool consentGiven = false)
    {
        var submission = new FormSubmission
        {
            PublicId = gen.NewIdRandom(),
            TenantId = tenantId,
            FormTemplateId = formTemplateId,
            FormTemplateVersion = formTemplateVersion,
            SubmissionData = submissionData,
            SubmittedBy = submittedBy,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = SubmissionStatus.Pending,
            ConsentGiven = consentGiven
        };
        submission.SetAuditCreate(submittedBy);
        return submission;
    }

    /// <summary>
    /// BP-Run factory — submissions captured under an active BusinessProcess WorkflowInstance.
    /// WorkflowInstanceId is required (BP Run container); WorkflowStateId optional (null = intake form, set = state form).
    /// Caller must verify WorkflowInstance.TenantId == tenantId BEFORE invocation (R14 cross-tenant guard).
    /// Each call inserts a NEW row; history retention is a feature (R1 Option c — query-latest pattern).
    /// </summary>
    public static FormSubmission CreateForBpRun(
        IGuidGenerator gen,
        Guid tenantId,
        Guid workflowInstanceId,
        long? workflowStateId,
        long formTemplateId,
        int formTemplateVersion,
        string? submissionData,
        Guid submittedBy,
        bool consentGiven = false)
    {
        var submission = new FormSubmission
        {
            PublicId = gen.NewIdRandom(),
            TenantId = tenantId,
            FormTemplateId = formTemplateId,
            FormTemplateVersion = formTemplateVersion,
            SubmissionData = submissionData,
            SubmittedBy = submittedBy,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = SubmissionStatus.Pending,
            ConsentGiven = consentGiven,
            WorkflowInstanceId = workflowInstanceId,
            WorkflowStateId = workflowStateId
        };
        submission.SetAuditCreate(submittedBy);
        return submission;
    }
}
