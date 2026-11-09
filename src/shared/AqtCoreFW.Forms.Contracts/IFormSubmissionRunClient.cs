namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for BP-Run scoped FormSubmission operations.
/// Consumed by BusinessProcess module (P3/P4 runtime endpoints, P6 prefill context).
/// Monolith: InProcessFormSubmissionRunClient (Dapper read + IMediator write delegation).
/// Microservice: gRPC client when Forms extracted.
/// Contract is FluentResults-free by design — outcomes use plain C# types compatible
/// with future gRPC / JSON transport.
///
/// Read pattern (R1 Option c): each edit = new row; client queries latest by SubmittedAt DESC
/// per (WorkflowInstanceId, WorkflowStateId, FormTemplateId). History retained.
/// Tenant scoping: all methods require tenantId; cross-tenant reads return null/empty
/// (do not leak existence). Write path enforces R14 cross-tenant guard against WorkflowInstance.TenantId.
/// </summary>
public interface IFormSubmissionRunClient
{
    /// <summary>
    /// Latest submission across all forms in a BP Run (intake + every state form).
    /// Returns one row per distinct FormTemplateId (most recent SubmittedAt).
    /// Used by P6 prefill context resolution.
    /// </summary>
    Task<IReadOnlyList<FormSubmissionSummaryDto>> GetLatestForRunAsync(
        Guid workflowInstanceId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Latest submission for a specific (instance, state, form) triple, or null if none.
    /// Used by BP transition handler "IsRequired form submitted?" gating check.
    /// </summary>
    Task<FormSubmissionSummaryDto?> GetLatestStateFormAsync(
        Guid workflowInstanceId,
        long workflowStateId,
        long formTemplateId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Create a submission under an existing BP Run (WorkflowInstance pre-created by BP module).
    /// workflowStateId null = intake form; non-null = state form.
    /// Delegates to SubmitFormCommand internally to preserve immutability + validation invariants.
    /// Returns a transport-neutral <see cref="BpRunSubmissionOutcome"/>: SubmissionId on success;
    /// ErrorCode GOV_BP_002 when WorkflowInstance.TenantId != tenantId (does not leak existence);
    /// FieldErrors when form validation fails. Expected outcomes do not throw — only genuinely
    /// unexpected faults (DB, infra) surface as exceptions (FluentResults-free contract, see header).
    ///
    /// <paramref name="signature"/> carries a digital-signature proof bundle when the form has a
    /// DigitalSignature field; null otherwise. The proof is verified at ingest against content
    /// recomputed server-side from <paramref name="data"/> — the bundle never carries the signed
    /// content. A failed verify yields a SubmissionFailed outcome with NO persistence.
    /// </summary>
    Task<BpRunSubmissionOutcome> CreateForBpRunAsync(
        Guid workflowInstanceId,
        long? workflowStateId,
        Guid formTemplatePublicId,
        Guid submittedBy,
        IReadOnlyDictionary<string, object?> data,
        bool consentGiven,
        Guid tenantId,
        BpRunSignaturePayload? signature = null,
        CancellationToken ct = default);

    /// <summary>
    /// Full submission history for a BP Run (all rows across all forms + states, latest first).
    /// Used by P6 admin/audit view.
    /// </summary>
    Task<IReadOnlyList<FormSubmissionSummaryDto>> GetAllForRunAsync(
        Guid workflowInstanceId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// BP MVP P6: full submission data dictionary keyed by FieldKey for the given submission.
    /// Routes to the correct storage backend based on the template's StorageMode (Json column vs
    /// dynamic Table). TableField nested rows are NOT attached — prefill is a single-value lookup
    /// per FieldKey. Returns null when the submission does not exist OR belongs to a different
    /// tenant (does not leak existence).
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>?> GetFullSubmissionAsync(
        Guid submissionPublicId,
        Guid tenantId,
        CancellationToken ct = default);
}
