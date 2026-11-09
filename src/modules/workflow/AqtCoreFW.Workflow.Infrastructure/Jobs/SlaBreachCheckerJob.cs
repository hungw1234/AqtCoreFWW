using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Events;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Infrastructure.Jobs;

/// <summary>
/// Hangfire job: scans for WorkflowInstances where DueAt has passed (instance-level SLA)
/// or where the current state's SlaHours threshold is exceeded (per-state SLA).
/// On breach: marks SlaBreachNotifiedAt + raises SlaBreachedEvent.
/// On per-state breach with AutoTransitionOnTimeoutId: executes the transition automatically.
/// Runs every 5 minutes. [DisableConcurrentExecution] prevents overlapping runs.
/// </summary>
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(300)]
public sealed class SlaBreachCheckerJob(
    IReadDbConnection db,
    WorkflowDbContext ctx,
    IGuidGenerator gen,
    ILogger<SlaBreachCheckerJob> logger)
{
    // Well-known system actor GUID for auto-timeout transitions
    private static readonly Guid SystemActorId = new("00000000-0000-0000-0000-000000000001");

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await HandleInstanceLevelBreachesAsync(ct);
        await HandlePerStateBreachesAsync(ct);
    }

    /// <summary>Instance-level SLA: DueAt has passed and SlaBreachNotifiedAt is null.</summary>
    private async Task HandleInstanceLevelBreachesAsync(CancellationToken ct)
    {
        // Select PublicId (T2 Guid AK) for event; DefinitionPublicId via JOIN to WorkflowDefinitions.
        const string sql = """
            SELECT i.Id, i.PublicId, i.CurrentStateId, i.DefinitionId,
                   d.PublicId AS DefinitionPublicId,
                   i.TenantId, i.CurrentStateEnteredAt,
                   DATEDIFF(HOUR, i.CurrentStateEnteredAt, GETUTCDATE()) AS ElapsedHours
            FROM workflow.WorkflowInstances i
            INNER JOIN workflow.WorkflowDefinitions d ON d.Id = i.DefinitionId
            WHERE i.CompletedAt IS NULL
              AND i.SlaBreachNotifiedAt IS NULL
              AND i.DueAt IS NOT NULL
              AND i.DueAt < GETUTCDATE()
              AND i.IsDeleted = 0
            """;

        var breached = (await db.QueryAsync<BreachedRow>(sql, cancellationToken: ct)).ToList();
        if (breached.Count == 0) return;

        logger.LogInformation("SLA breach checker (instance-level) found {Count} breached instances.", breached.Count);

        int saved = 0, skipped = 0;
        foreach (var row in breached)
        {
            var instance = await ctx.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == row.Id, ct);
            if (instance is null) continue;

            instance.MarkSlaBreachNotified();
            instance.AddDomainEvent(new SlaBreachedEvent(
                row.PublicId, row.CurrentStateId, row.DefinitionPublicId,
                row.TenantId, row.CurrentStateEnteredAt, row.ElapsedHours,
                gen.NewIdMonotonic()));

            // Per-instance save — EF concurrency token on CurrentStateId fires on ANY update
            // to this row, so batch save would poison the job on a single concurrent user transition.
            try
            {
                await ctx.SaveChangesAsync(ct);
                saved++;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                skipped++;
                logger.LogWarning(ex,
                    "SLA notification skipped for instance {InstanceId} — concurrent user transition detected. Retry next cycle.",
                    instance.Id);
                ctx.Entry(instance).State = EntityState.Detached;
            }
        }

        logger.LogInformation(
            "SLA breach notifications: {Saved} marked, {Skipped} skipped (concurrent transitions), {Total} total.",
            saved, skipped, breached.Count);
    }

    /// <summary>
    /// Per-state SLA: instances whose current state has SlaHours set
    /// and CurrentStateEnteredAt + SlaHours &lt; now.
    /// If AutoTransitionOnTimeoutId is set on the state, executes the transition automatically.
    /// </summary>
    private async Task HandlePerStateBreachesAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT i.Id AS InstanceId, i.CurrentStateId, i.DefinitionId,
                   s.SlaHours, s.AutoTransitionOnTimeoutId,
                   DATEDIFF(HOUR, i.CurrentStateEnteredAt, GETUTCDATE()) AS ElapsedHours
            FROM workflow.WorkflowInstances i
            JOIN workflow.WorkflowStates s ON s.Id = i.CurrentStateId
            WHERE i.CompletedAt IS NULL
              AND i.IsDeleted = 0
              AND s.SlaHours IS NOT NULL
              AND DATEADD(HOUR, s.SlaHours, i.CurrentStateEnteredAt) < GETUTCDATE()
            """;

        var rows = (await db.QueryAsync<StateSlaRow>(sql, cancellationToken: ct)).ToList();
        if (rows.Count == 0) return;

        logger.LogInformation("SLA breach checker (per-state) found {Count} instances.", rows.Count);

        foreach (var row in rows)
        {
            if (row.AutoTransitionOnTimeoutId is null) continue;

            var instance = await ctx.WorkflowInstances
                .FirstOrDefaultAsync(i => i.Id == row.InstanceId, ct);

            // CurrentStateId: long (T1 WorkflowState.Id — both sides are long)
            if (instance is null || instance.CurrentStateId != row.CurrentStateId) continue;

            // AutoTransitionOnTimeoutId: long? (T1 WorkflowTransition.Id FK)
            var transition = await ctx.WorkflowTransitions
                .FirstOrDefaultAsync(t => t.Id == row.AutoTransitionOnTimeoutId!.Value, ct);

            if (transition is null || transition.FromStateId != row.CurrentStateId) continue;

            var targetState = await ctx.WorkflowStates
                .FirstOrDefaultAsync(s => s.Id == transition.ToStateId, ct);

            if (targetState is null) continue;

            logger.LogInformation(
                "Auto-timeout: executing transition {TransitionId} on instance {InstanceId}.",
                transition.Id, instance.Id);

            instance.ApplyTransition(
                gen,
                transition,
                targetState.IsFinal,
                SystemActorId,
                "System (Auto-timeout)");

            // Parity with the interactive transition path: a FormSubmission workflow finalized by
            // auto-timeout must also route its approve/reject through the outbox (the removed in-process
            // WorkflowTransitionedFormSubmissionHandler fired on any transition, this path included).
            // No-op for non-FormSubmission entities. Captured by OutboxInterceptor in the save below.
            if (targetState.IsFinal)
                instance.RaiseFinalizationEvent(targetState.Name, SystemActorId, gen);

            // Per-instance save — same rationale as HandleInstanceLevelBreachesAsync.
            // EF concurrency token on CurrentStateId is critical here: a concurrent user
            // transition would mean the state already changed, making the auto-timeout stale.
            try
            {
                await ctx.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex,
                    "Auto-timeout transition skipped for instance {InstanceId} — concurrent user transition already advanced state. Retry next cycle.",
                    instance.Id);
                ctx.Entry(instance).State = EntityState.Detached;
            }
        }
    }

    // T2 WorkflowInstance: Id=long bigint PK, DefinitionId=long FK, CurrentStateId=long FK.
    // PublicId=Guid (T2 AK — used in SlaBreachedEvent). TenantId stays Guid (FK exception).
    // DefinitionPublicId=Guid (from JOIN to WorkflowDefinitions — used in SlaBreachedEvent).
    private sealed class BreachedRow
    {
        public long Id { get; set; }
        public Guid PublicId { get; set; }
        public long CurrentStateId { get; set; }
        public long DefinitionId { get; set; }
        public Guid DefinitionPublicId { get; set; }
        public Guid TenantId { get; set; }
        public DateTimeOffset CurrentStateEnteredAt { get; set; }
        public int ElapsedHours { get; set; }
    }

    // T1 WorkflowState: SlaHours, AutoTransitionOnTimeoutId are long? FK.
    private sealed class StateSlaRow
    {
        public long InstanceId { get; set; }
        public long CurrentStateId { get; set; }
        public long DefinitionId { get; set; }
        public int SlaHours { get; set; }
        public long? AutoTransitionOnTimeoutId { get; set; }
        public int ElapsedHours { get; set; }
    }
}
