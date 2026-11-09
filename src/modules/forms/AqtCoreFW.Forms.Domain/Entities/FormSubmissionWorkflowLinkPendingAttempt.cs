using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// Persistent record of a FormSubmission whose linked workflow-instance creation
/// failed (5b fail-CLOSED graceful-skip — empty handler pool, GOV_WFL_014). One row
/// per orphan submission; idempotency unique key = (SubmissionId, TenantId).
///
/// <para><b>Why a separate entity (Q6 ratified 2026-05-22 v2.137p101):</b> preserves
/// <see cref="FormSubmission"/>'s immutability invariant (FormSubmission.cs:9-19); retry
/// state (AttemptCount / NextRetryAt / Status) is orthogonal to the submission's
/// domain. Lift-from PHR-V2 DCAND-a separate-entity precedent.</para>
///
/// <para><b>Cross-module FK (none enforced at DB):</b> <see cref="ApprovalWorkflowDefinitionId"/>
/// references workflow.WorkflowDefinitions.PublicId via Guid AK — stored as plain
/// Guid column without EF nav-prop nor DB FK constraint (cross-module-by-Guid AK
/// convention; matches FormSubmission.WorkflowInstanceId pattern).</para>
///
/// <para><b>Lifecycle:</b> Pending → Retrying (auto-job claims) → Succeeded (link
/// established, terminal) OR Shelved (max retries hit, admin-managed terminal).</para>
/// </summary>
public sealed class FormSubmissionWorkflowLinkPendingAttempt : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; private set; }

    /// <summary>FK by Guid AK to <see cref="FormSubmission.PublicId"/>. No EF nav-prop.</summary>
    public Guid SubmissionId { get; private set; }

    /// <summary>FK to <see cref="FormTemplate"/>.Id (T1 long PK).</summary>
    public long FormTemplateId { get; private set; }

    /// <summary>Cross-module FK by Guid AK to workflow.WorkflowDefinitions.PublicId. No EF nav-prop.</summary>
    public Guid ApprovalWorkflowDefinitionId { get; private set; }

    /// <summary>Original submitter; <c>Guid.Empty</c> for public/anonymous submissions.</summary>
    public Guid SubmittedBy { get; private set; }

    /// <summary>0 at first insert; auto-retry job increments on each attempt.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Last failure code (e.g. <c>GOV_WFL_014</c>). Updated on each retry.</summary>
    public string LastFailureCode { get; private set; } = string.Empty;

    /// <summary>Last failure message; sanitized to 500 chars at update site.</summary>
    public string? LastError { get; private set; }

    /// <summary>Set by retry-job scheduler; null when manual-only mode or terminal status.</summary>
    public DateTimeOffset? NextRetryAt { get; private set; }

    public WorkflowLinkPendingStatus Status { get; private set; } = WorkflowLinkPendingStatus.Pending;

    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedBy { get; private set; }

    private FormSubmissionWorkflowLinkPendingAttempt() { } // EF Core

    public static FormSubmissionWorkflowLinkPendingAttempt CreateFromEvent(
        Guid tenantId,
        Guid submissionId,
        long formTemplateId,
        Guid approvalWorkflowDefinitionId,
        Guid submittedBy,
        string failureCode,
        string? failureMessage)
    {
        var entity = new FormSubmissionWorkflowLinkPendingAttempt
        {
            TenantId = tenantId,
            SubmissionId = submissionId,
            FormTemplateId = formTemplateId,
            ApprovalWorkflowDefinitionId = approvalWorkflowDefinitionId,
            SubmittedBy = submittedBy,
            AttemptCount = 0,
            LastFailureCode = failureCode,
            LastError = Truncate(failureMessage),
            Status = WorkflowLinkPendingStatus.Pending
        };
        // System-attributed: no human actor on initial signal capture.
        entity.SetAuditCreate(Guid.Empty);
        return entity;
    }

    /// <summary>
    /// Re-emit of the same orphan (at-least-once forward-compat with Outbox migration):
    /// preserve original CreatedAt/AttemptCount; only refresh LastFailureCode/LastError
    /// + clear terminal Status if it had been Succeeded/Shelved (caller decides whether
    /// to call this — UPSERT handler reads Status and short-circuits if terminal).
    /// </summary>
    public void RefreshFromRetryFailure(string failureCode, string? failureMessage, Guid systemActor)
    {
        LastFailureCode = failureCode;
        LastError = Truncate(failureMessage);
        SetAuditUpdate(systemActor);
    }

    // --- Phase 04: retry-job lifecycle mutators (Pending → Retrying → Succeeded|Shelved) ---

    /// <summary>
    /// Idempotent claim marker: the retry job calls this right after claiming a due row, before
    /// attempting the resolve-or-create step. Legal from <see cref="WorkflowLinkPendingStatus.Pending"/>
    /// or <see cref="WorkflowLinkPendingStatus.Retrying"/> (a re-claimed Retrying row is legal on
    /// crash-recovery — the prior attempt died mid-flight without reaching a terminal state).
    /// </summary>
    public void MarkRetrying(Guid systemActor)
    {
        if (Status is not (WorkflowLinkPendingStatus.Pending or WorkflowLinkPendingStatus.Retrying))
            throw new InvalidOperationException(
                $"GOV_FRM_040: Không thể đánh dấu Retrying từ trạng thái {Status}.");

        Status = WorkflowLinkPendingStatus.Retrying;
        SetAuditUpdate(systemActor);
    }

    /// <summary>
    /// Terminal success: the workflow instance link is now established (either newly created, or
    /// resolved via the idempotency check against an already-existing instance — M1 throw-after-create
    /// case). <paramref name="instanceId"/> is recorded via audit/log only; this entity has no
    /// InstanceId column by design (YAGNI — the link of record lives on
    /// <see cref="FormSubmission.WorkflowInstanceId"/>).
    /// </summary>
    public void MarkSucceeded(Guid instanceId, Guid actor)
    {
        _ = instanceId; // recorded by the caller's log statement, not persisted on this row.

        if (Status is not (WorkflowLinkPendingStatus.Pending or WorkflowLinkPendingStatus.Retrying))
            throw new InvalidOperationException(
                $"GOV_FRM_041: Không thể đánh dấu Succeeded từ trạng thái {Status}.");

        Status = WorkflowLinkPendingStatus.Succeeded;
        ResolvedAt = DateTimeOffset.UtcNow;
        ResolvedBy = actor;
        NextRetryAt = null;
        SetAuditUpdate(actor);
    }

    /// <summary>
    /// Terminal dead-letter: max retry attempts exhausted. The submission itself is left untouched
    /// (still discoverable, never cancelled — LOCKED Q2). Auto-job never un-shelves; only
    /// <see cref="ReactivateForManualRetry"/> (admin path) can revive a Shelved row.
    /// </summary>
    public void MarkShelved(Guid actor)
    {
        if (Status is not (WorkflowLinkPendingStatus.Pending or WorkflowLinkPendingStatus.Retrying))
            throw new InvalidOperationException(
                $"GOV_FRM_042: Không thể đánh dấu Shelved từ trạng thái {Status}.");

        Status = WorkflowLinkPendingStatus.Shelved;
        ResolvedAt = DateTimeOffset.UtcNow;
        ResolvedBy = actor;
        NextRetryAt = null;
        SetAuditUpdate(actor);
    }

    /// <summary>
    /// Re-arms the row for the next attempt after a failed create/resolve. Increments
    /// <see cref="AttemptCount"/>, records the failure, and sets <see cref="NextRetryAt"/> to the
    /// caller-computed backoff instant. Status stays/returns to Pending (re-armed, claimable again).
    /// </summary>
    public void RecordRetryFailure(string code, string? msg, DateTimeOffset nextRetryAt, Guid systemActor)
    {
        if (Status is not (WorkflowLinkPendingStatus.Pending or WorkflowLinkPendingStatus.Retrying))
            throw new InvalidOperationException(
                $"GOV_FRM_043: Không thể ghi nhận thất bại retry từ trạng thái {Status}.");

        AttemptCount++;
        LastFailureCode = code;
        LastError = Truncate(msg);
        NextRetryAt = nextRetryAt;
        Status = WorkflowLinkPendingStatus.Pending;
        SetAuditUpdate(systemActor);
    }

    /// <summary>
    /// Manual admin path ONLY (Phase-05 Retry-now on a Shelved row — LOCKED Q4). The auto Hangfire
    /// job NEVER calls this; it treats Shelved as terminal. Un-shelves back to Pending, armed for
    /// immediate claim on the job's next tick.
    /// </summary>
    public void ReactivateForManualRetry(Guid actor)
    {
        if (Status != WorkflowLinkPendingStatus.Shelved)
            throw new InvalidOperationException(
                $"GOV_FRM_044: Chỉ có thể kích hoạt lại thủ công từ trạng thái Shelved (hiện tại: {Status}).");

        Status = WorkflowLinkPendingStatus.Pending;
        NextRetryAt = DateTimeOffset.UtcNow;
        ResolvedAt = null;
        ResolvedBy = null;
        SetAuditUpdate(actor);
    }

    /// <summary>
    /// Manual admin path (Phase-05 Retry-now on a Pending/Retrying row). Arms an immediate claim on
    /// the job's next tick WITHOUT incrementing <see cref="AttemptCount"/> — a manual nudge is not a
    /// failed attempt. Kept separate from <see cref="RecordRetryFailure"/> so failure-counting
    /// semantics stay clean.
    /// </summary>
    public void ArmForImmediateRetry(Guid actor)
    {
        if (Status is not (WorkflowLinkPendingStatus.Pending or WorkflowLinkPendingStatus.Retrying))
            throw new InvalidOperationException(
                $"GOV_FRM_045: Không thể kích hoạt retry ngay từ trạng thái {Status}.");

        NextRetryAt = DateTimeOffset.UtcNow;
        SetAuditUpdate(actor);
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length > 500 ? value[..500] : value;
    }
}
