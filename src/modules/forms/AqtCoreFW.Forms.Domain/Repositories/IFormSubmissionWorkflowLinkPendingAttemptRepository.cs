using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;

namespace AqtCoreFW.Forms.Domain.Repositories;

/// <summary>
/// Pending-workflow-link attempt repository — write path + idempotency lookup.
/// Query handlers (admin UI) bypass this and use IReadDbConnection (Dapper) directly.
/// </summary>
public interface IFormSubmissionWorkflowLinkPendingAttemptRepository
    : IRepository<FormSubmissionWorkflowLinkPendingAttempt, long>
{
    /// <summary>
    /// Idempotency lookup. Returns the existing attempt row for this submission within
    /// the current tenant scope, or null. Used by <c>WorkflowLinkPendingEventHandler</c>
    /// UPSERT path. NoTracking — caller decides whether to mutate (re-attaches if so).
    /// </summary>
    Task<FormSubmissionWorkflowLinkPendingAttempt?> GetBySubmissionIdAsync(
        Guid submissionId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracked lookup by (SubmissionId, TenantId) for the Phase-05 admin manual-override commands
    /// (Retry-now / Shelve). Unlike <see cref="GetBySubmissionIdAsync"/> (NoTracking, idempotency
    /// lookup), this returns a tracked instance so the caller can invoke a lifecycle mutator
    /// (<c>ArmForImmediateRetry</c> / <c>ReactivateForManualRetry</c> / <c>MarkShelved</c>) and persist
    /// via <c>UpdateAsync</c> on the same context. Null when no row exists for this submission+tenant.
    /// </summary>
    Task<FormSubmissionWorkflowLinkPendingAttempt?> GetTrackedBySubmissionIdAsync(
        Guid submissionId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claim query for <c>FormWorkflowLinkRetryJob</c> — rows due for retry ACROSS ALL TENANTS (the job
    /// runs with no tenant principal; it pins RLS per-row when it writes). Tracked (not NoTracking) —
    /// caller mutates via the entity's lifecycle mutators and persists through this same context.
    ///
    /// Due predicate: <c>Status IN (Pending, Retrying)</c> AND
    /// <c>(NextRetryAt IS NULL OR NextRetryAt &lt;= now)</c> AND <c>AttemptCount &lt; maxAttempts</c>,
    /// ordered by NextRetryAt ascending (nulls first — brand-new orphans claimed before older re-armed
    /// ones), capped at <paramref name="batchSize"/>.
    ///
    /// The <c>NextRetryAt IS NULL</c> branch is REQUIRED: <c>CreateFromEvent</c> inserts a fresh row
    /// with NextRetryAt null, and the filtered index
    /// <c>IX_FormSubmissionWfLinkPendingAttempts_Tenant_Status_NextRetryAt</c> only covers
    /// <c>NextRetryAt IS NOT NULL</c> — dropping the NULL branch would silently never claim new orphans
    /// (see plan Blast-Radius axis 1, "backward trap").
    /// </summary>
    Task<IReadOnlyList<FormSubmissionWorkflowLinkPendingAttempt>> GetDueAttemptsAsync(
        DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default);
}
