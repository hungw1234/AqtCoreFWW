using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Infrastructure.Jobs;

/// <summary>
/// Hangfire job: finds tasks with expired delegations and auto-returns them to original assignee.
/// Runs every 15 minutes. Idempotent — F08 guard in ReturnDelegation() skips if not delegated.
/// F04 reminder: ChangeTracker.Clear() after each task to prevent cross-task state leakage.
/// [DisableConcurrentExecution] prevents overlapping runs during slow DB scans.
/// </summary>
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(600)]
public sealed class DelegationExpiryJob(
    IReadDbConnection db,
    WorkflowDbContext ctx,
    ILogger<DelegationExpiryJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Find tasks currently delegated where the delegation rule has expired
        const string sql = """
            SELECT t.Id, t.DelegatedFromUserId, t.AssigneeId, t.TenantId
            FROM workflow.WorkflowTasks t
            WHERE t.DelegatedFromUserId IS NOT NULL
              AND t.IsDeleted = 0
              AND t.Status IN (0, 1)
              AND EXISTS (
                  SELECT 1 FROM workflow.DelegationRules dr
                  WHERE dr.DelegatorUserId = t.DelegatedFromUserId
                    AND dr.TenantId = t.TenantId
                    AND dr.IsActive = 1
                    AND dr.ValidUntil < SYSDATETIMEOFFSET()
              )
            """;

        var expiredRows = (await db.QueryAsync<ExpiredDelegationRow>(sql, cancellationToken: ct)).ToList();

        if (expiredRows.Count == 0)
            return;

        logger.LogInformation("DelegationExpiryJob found {Count} expired delegations to return.", expiredRows.Count);

        var systemActor = Guid.Empty;

        foreach (var row in expiredRows)
        {
            // H3: IgnoreQueryFilters — job runs outside HTTP context; ITenantContext is empty.
            // FindAsync bypasses global filters anyway; use explicit filter-ignored LINQ query
            // so EF attaches the entity correctly for SaveChanges while still locating it.
            var task = await ctx.WorkflowTasks
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == row.Id, ct);
            if (task is null)
                continue;

            // F08: idempotency guard — skip if already returned (domain method handles this)
            if (!task.DelegatedFromUserId.HasValue)
            {
                ctx.ChangeTracker.Clear(); // F04: prevent stale state bleeding across items
                continue;
            }

            var delegateUserId = task.AssigneeId ?? systemActor;
            task.ReturnDelegation(systemActor);

            var assignment = TaskAssignment.Create(
                task.Id,
                systemActor,
                delegateUserId,
                TaskAssignmentType.Return,
                "delegation-expired");

            await ctx.TaskAssignments.AddAsync(assignment, ct);

            logger.LogInformation(
                "Returned delegation for task {TaskId} (original assignee: {OriginalId}).",
                task.Id, row.DelegatedFromUserId);

            // F04: clear tracker after each item to avoid cross-task EF state issues
            await ctx.SaveChangesAsync(ct);
            ctx.ChangeTracker.Clear();
        }
    }

    // T1 WorkflowTask: Id=long (bigint PK via Entity base).
    // DelegatedFromUserId, AssigneeId: Guid? (identity FK exception). TenantId: Guid.
    private sealed class ExpiredDelegationRow
    {
        public long Id { get; set; }
        public Guid? DelegatedFromUserId { get; set; }
        public Guid? AssigneeId { get; set; }
        public Guid TenantId { get; set; }
    }
}
