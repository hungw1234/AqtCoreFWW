using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Infrastructure.Jobs;

/// <summary>
/// Hangfire job: finds overdue WorkflowTasks and applies EscalationRules.
/// Runs every 10 minutes. For each overdue open/in-progress task, checks if
/// any active escalation rule threshold has been exceeded and creates a
/// TaskAssignment record of type Escalation.
/// S9 RT2-7: Pool tasks (AssigneeId IS NULL) are excluded from the escalation pass — they cannot
/// be stolen by escalation. A second aging pass (RT3-M3) emits LogWarning for unclaimed pool tasks.
/// [DisableConcurrentExecution] prevents overlapping runs during slow DB scans.
/// </summary>
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(600)]
public sealed class EscalationCheckJob(
    IReadDbConnection db,
    WorkflowDbContext ctx,
    ILogger<EscalationCheckJob> logger)
{
    /// <summary>
    /// Minutes a pool task may remain unclaimed before a structured warning is logged.
    /// Tuned to typical task SLA (60 min). NOT promoted to per-tenant config (P5 future).
    /// Log-only — no DB write, no event, no schema. Pool aging discoverable via log scrape.
    /// </summary>
    private const int PoolAgingThresholdMinutes = 60;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // ── Pass 1: escalation — overdue tasks with AssigneeId IS NOT NULL ───────
        // Pool tasks (AssigneeId IS NULL) are excluded by SQL filter + belt-and-braces guard.
        const string sql = """
            SELECT t.Id, t.WorkflowInstanceId, t.AssigneeId, t.TenantId,
                   i.DefinitionId,
                   DATEDIFF(MINUTE, t.CreatedAt, GETUTCDATE()) AS MinutesElapsed
            FROM workflow.WorkflowTasks t
            INNER JOIN workflow.WorkflowInstances i ON i.Id = t.WorkflowInstanceId
            WHERE t.Status IN (0, 1)
              AND t.IsDeleted = 0
              AND t.DueDate IS NOT NULL
              AND t.DueDate < GETUTCDATE()
              AND t.AssigneeId IS NOT NULL  -- S9 RT2-7: pool tasks excluded from escalation pass
            """;

        var overdueTasks = (await db.QueryAsync<OverdueTaskRow>(sql, cancellationToken: ct)).ToList();

        if (overdueTasks.Count > 0)
        {
            logger.LogInformation("EscalationCheckJob found {Count} overdue tasks.", overdueTasks.Count);

            foreach (var row in overdueTasks)
            {
                // S9 RT2-7 belt-and-braces: SQL filter above already excludes pool tasks,
                // but this guard defends against future SQL drift reintroducing the steal-bug.
                // Pool tasks NEVER receive escalation ledger rows or Assign() calls.
                if (row.AssigneeId is null)
                    continue;

                // Load active escalation rules for this definition — scoped to tenant
                var rules = await ctx.EscalationRules
                    .Where(r => r.WorkflowDefinitionId == row.DefinitionId
                             && r.TenantId == row.TenantId
                             && r.IsActive
                             && r.EscalationAfterMinutes <= row.MinutesElapsed)
                    .OrderBy(r => r.EscalationAfterMinutes)
                    .ToListAsync(ct);

                if (rules.Count == 0)
                    continue;

                // Apply the most specific rule (highest threshold reached)
                var rule = rules.Last();

                // System actor ID — represents automated escalation
                var systemActorId = Guid.Empty;

                // Determine escalation target userId (best-effort: use EscalateToValue as Guid)
                if (!Guid.TryParse(rule.EscalateToValue, out var escalateToId))
                    escalateToId = Guid.Empty; // fallback — real impl would resolve via role service

                var assignment = TaskAssignment.Create(
                    row.Id,
                    systemActorId,
                    escalateToId,
                    TaskAssignmentType.Escalation,
                    $"Auto-escalated after {row.MinutesElapsed} minutes overdue. Rule: {rule.EscalateTo}={rule.EscalateToValue}");

                await ctx.TaskAssignments.AddAsync(assignment, ct);

                // Update task assignee to escalation target (if resolved)
                if (escalateToId != Guid.Empty)
                {
                    var task = await ctx.WorkflowTasks.FindAsync([row.Id], ct);
                    task?.Assign(escalateToId, systemActorId);
                }

                logger.LogInformation(
                    "Escalated task {TaskId} to {Target} after {Minutes} minutes.",
                    row.Id, rule.EscalateToValue, row.MinutesElapsed);
            }

            await ctx.SaveChangesAsync(ct);
        }

        // ── Pass 2: RT3-M3 pool-aging (log-only) ─────────────────────────────────
        // Scans null-assignee tasks past PoolAgingThresholdMinutes; emits structured LogWarning
        // per task. No steal, no ledger write, no DB mutation. Two passes are disjoint:
        // Pass 1 covers AssigneeId IS NOT NULL; Pass 2 covers AssigneeId IS NULL.
        const string poolAgingSql = """
            SELECT t.Id, t.WorkflowInstanceId, t.AssigneeId, t.TenantId,
                   i.DefinitionId,
                   DATEDIFF(MINUTE, t.CreatedAt, GETUTCDATE()) AS MinutesElapsed
            FROM workflow.WorkflowTasks t
            INNER JOIN workflow.WorkflowInstances i ON i.Id = t.WorkflowInstanceId
            WHERE t.Status IN (0, 1)
              AND t.IsDeleted = 0
              AND t.AssigneeId IS NULL
              AND DATEDIFF(MINUTE, t.CreatedAt, GETUTCDATE()) >= @threshold
            """;

        var agedPoolTasks = (await db.QueryAsync<OverdueTaskRow>(
            poolAgingSql,
            new { threshold = PoolAgingThresholdMinutes },
            cancellationToken: ct)).ToList();

        if (agedPoolTasks.Count == 0)
            return;

        logger.LogInformation("EscalationCheckJob found {Count} aged pool tasks.", agedPoolTasks.Count);

        foreach (var row in agedPoolTasks)
        {
            logger.LogWarning(
                "Pool task {TaskId} (instance {InstanceId}, tenant {TenantId}) unclaimed for "
              + "{Minutes} minutes — exceeds aging threshold {ThresholdMinutes} minutes.",
                row.Id, row.WorkflowInstanceId, row.TenantId, row.MinutesElapsed, PoolAgingThresholdMinutes);
        }
    }

    // T1 WorkflowTask: Id=long, WorkflowInstanceId=long (FK to WorkflowInstance.Id bigint PK).
    // DefinitionId=long (FK to WorkflowDefinition.Id bigint PK via WorkflowInstance join).
    // AssigneeId stays Guid (identity FK exception). TenantId stays Guid.
    private sealed class OverdueTaskRow
    {
        public long Id { get; set; }
        public long WorkflowInstanceId { get; set; }
        public Guid? AssigneeId { get; set; }
        public Guid TenantId { get; set; }
        public long DefinitionId { get; set; }
        public int MinutesElapsed { get; set; }
    }
}
