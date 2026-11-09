using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Forms.Infrastructure.Persistence;

public sealed class FormSubmissionWorkflowLinkPendingAttemptRepository(
    FormsDbContext dbContext,
    ICrossModuleUnitOfWork? uow = null)
    : GenericRepository<FormSubmissionWorkflowLinkPendingAttempt, long>(dbContext, uow),
      IFormSubmissionWorkflowLinkPendingAttemptRepository
{
    private FormsDbContext Db => (FormsDbContext)Context;

    public async Task<FormSubmissionWorkflowLinkPendingAttempt?> GetBySubmissionIdAsync(
        Guid submissionId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // 5c-A INVARIANT: explicit NoTracking on read for idempotency lookup; handler
        // re-attaches if it needs to mutate. Global tenant filter on FormsDbContext
        // adds WHERE TenantId = ctx.TenantId, but we keep the explicit predicate here
        // for self-documenting intent + defense-in-depth against 4-C-A model-cache drift.
        return await Db.FormSubmissionWorkflowLinkPendingAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.SubmissionId == submissionId && a.TenantId == tenantId,
                cancellationToken);
    }

    /// <summary>
    /// Phase 05 admin manual-override lookup — tracked (no <c>AsNoTracking</c>) so the command handler
    /// can call a lifecycle mutator directly on the returned instance and persist via
    /// <c>UpdateAsync</c> in the same scope. FormsDbContext's global tenant filter already scopes to
    /// the ambient tenant; the explicit <c>TenantId</c> predicate is kept for defense-in-depth,
    /// matching <see cref="GetBySubmissionIdAsync"/>'s convention.
    /// </summary>
    public async Task<FormSubmissionWorkflowLinkPendingAttempt?> GetTrackedBySubmissionIdAsync(
        Guid submissionId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);

        return await Db.FormSubmissionWorkflowLinkPendingAttempts
            .FirstOrDefaultAsync(
                a => a.SubmissionId == submissionId && a.TenantId == tenantId,
                cancellationToken);
    }

    /// <summary>
    /// Phase 04 claim query — see interface doc for the due predicate + the NextRetryAt-IS-NULL trap.
    ///
    /// Cross-tenant by design: <c>IgnoreQueryFilters()</c> bypasses FormsDbContext's global tenant
    /// filter (the job runs with no ambient tenant principal) so it can see due rows for EVERY
    /// tenant in one scan. This is safe because it is READ-ONLY here — the job pins RLS per-row via
    /// <c>ICrossModuleUnitOfWork.BeginAsync(row.TenantId, ct)</c> before it WRITES (mutates + saves)
    /// any claimed row; the write is what RLS must gate, not this claim read.
    ///
    /// Tracked (no <c>AsNoTracking</c>) — the job calls the entity's lifecycle mutators
    /// (MarkRetrying / MarkSucceeded / MarkShelved / RecordRetryFailure) and persists the same
    /// tracked instance via <c>UpdateAsync</c> on the pinned per-row scope.
    /// </summary>
    public async Task<IReadOnlyList<FormSubmissionWorkflowLinkPendingAttempt>> GetDueAttemptsAsync(
        DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);

        return await Db.FormSubmissionWorkflowLinkPendingAttempts
            .IgnoreQueryFilters()
            .Where(a =>
                (a.Status == WorkflowLinkPendingStatus.Pending
                    || a.Status == WorkflowLinkPendingStatus.Retrying)
                && (a.NextRetryAt == null || a.NextRetryAt <= now))
            .OrderBy(a => a.NextRetryAt) // nulls first (SQL Server ASC sorts NULL before non-NULL)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }
}
