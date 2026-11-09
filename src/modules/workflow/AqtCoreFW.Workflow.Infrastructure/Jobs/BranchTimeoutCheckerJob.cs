using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Infrastructure.Jobs;

/// <summary>
/// Hangfire job: finds InProgress branch statuses where timeout has expired,
/// marks them TimedOut, and resolves all pending children as Timeout.
/// Runs every 5 minutes. [DisableConcurrentExecution] prevents overlapping scans.
/// </summary>
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(300)]
public sealed class BranchTimeoutCheckerJob(
    IReadDbConnection db,
    WorkflowDbContext ctx,
    ILogger<BranchTimeoutCheckerJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Find branch statuses that are InProgress and past their timeout threshold.
        // TimeoutMinutes = 0 on the branch definition means no timeout — excluded via > 0 guard.
        const string sql = """
            SELECT bs.Id
            FROM workflow.WorkflowInstanceBranchStatuses bs
            INNER JOIN workflow.WorkflowParallelBranches pb ON pb.Id = bs.ParallelBranchId
            WHERE bs.Status = 'InProgress'
              AND pb.TimeoutMinutes > 0
              AND DATEADD(MINUTE, pb.TimeoutMinutes, bs.CreatedAt) < GETUTCDATE()
            """;

        // WorkflowInstanceBranchStatus.Id is long (T1 bigint PK via Entity base).
        var expiredIds = (await db.QueryAsync<long>(sql, cancellationToken: ct)).ToList();

        if (expiredIds.Count == 0)
            return;

        logger.LogInformation(
            "BranchTimeoutCheckerJob found {Count} expired branch statuses.", expiredIds.Count);

        foreach (var branchStatusId in expiredIds)
        {
            var branchStatus = await ctx.InstanceBranchStatuses
                .Include(s => s.ChildStatuses)
                .FirstOrDefaultAsync(s => s.Id == branchStatusId, ct);

            if (branchStatus is null || branchStatus.Status != WorkflowInstanceBranchStatus.Statuses.InProgress)
                continue;

            // Resolve all still-pending children as Timeout
            // WorkflowBranchChildStatus.Id is long (T1 entity via Entity base).
            var timedOutChildIds = new List<long>();
            foreach (var child in branchStatus.ChildStatuses.Where(c => !c.ResolutionType.HasValue))
            {
                child.Resolve(BranchResolutionType.Timeout, Guid.Empty, "Branch timed out.");
                timedOutChildIds.Add(child.Id);
            }

            branchStatus.MarkTimedOut();

            // Cancel linked WorkflowTasks for timed-out children
            if (timedOutChildIds.Count > 0)
            {
                var linkedTasks = await ctx.WorkflowTasks
                    .Where(t => t.BranchChildStatusId.HasValue
                        && timedOutChildIds.Contains(t.BranchChildStatusId.Value)
                        && (t.Status == WorkflowTaskStatus.Open || t.Status == WorkflowTaskStatus.InProgress))
                    .ToListAsync(ct);

                foreach (var task in linkedTasks)
                    task.Cancel("Branch timed out", Guid.Empty);
            }

            logger.LogInformation(
                "Branch status {BranchStatusId} marked TimedOut for instance {InstanceId}.",
                branchStatus.Id, branchStatus.InstanceId);
        }

        await ctx.SaveChangesAsync(ct);
    }
}
