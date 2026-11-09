using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using Hangfire;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Infrastructure.Jobs;

/// <summary>
/// Hangfire reconciler: finishes parallel-branch joins that satisfied their join condition but whose
/// instance never advanced to the continuation state — a strand left by a crash between the child
/// resolution commit and the join transition, or by a genuine transient concurrency conflict that the
/// synchronous winner-election surfaced for an async finisher (BranchJoinCompletionService winner-election
/// step 2 emits exactly this case). Completion is delegated to IBranchJoinCompletionService so the
/// RowVersion winner-election, the continuation pre-check (fail-CLOSED) and continuation staging stay in
/// one place and remain concurrency-safe against the live synchronous resolve path — the reconciler never
/// reimplements join completion.
///
/// Scope is intentionally narrow: ONLY branches still InProgress whose join condition is now met and whose
/// instance is still parked at the branching state. A timed-out branch (status TimedOut) or an unmet join
/// is deliberately untouched — advancing a failed join would require a failure-target state the engine
/// does not model.
///
/// A permanently-failing continuation (empty / handler-less continuation pool = a definition config error)
/// cannot be fixed by retrying; it is logged at most once per strand per throttle window (IMemoryCache) so
/// the 5-minute cadence does not spam logs, then left for an operator to fix the definition.
///
/// Runs every 5 minutes. [DisableConcurrentExecution] prevents overlapping scans. Ships dormant: with no
/// live parallel definition the candidate scan returns zero rows.
/// </summary>
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(300)]
public sealed class BranchJoinReconcilerJob(
    IReadDbConnection db,
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache,
    ILogger<BranchJoinReconcilerJob> logger)
{
    // Well-known system actor for reconciler-driven continuation transitions (mirrors SlaBreachCheckerJob).
    private static readonly Guid SystemActorId = new("00000000-0000-0000-0000-000000000001");

    // Throttle window for the permanent-config-strand error log — one error per strand per window, not one
    // per 5-minute cycle. In-memory by design (no persistence); a process restart simply re-arms it.
    private static readonly TimeSpan PermanentStrandLogWindow = TimeSpan.FromHours(1);

    private enum ReconcileOutcome { Skipped, Completed, Transient, Permanent }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Candidate scan (raw SQL — bypasses EF tracking and sees all tenants, like the sibling branch
        // jobs). Narrow predicate: branch InProgress + instance not advanced (still parked at the branching
        // state). The full join condition (AND-all-approved / OR-any / N-of-M) cannot be expressed cheaply
        // in SQL, so it is re-checked in memory per candidate via CheckJoinCondition.
        const string sql = """
            SELECT i.PublicId
            FROM workflow.WorkflowInstanceBranchStatuses bs
            INNER JOIN workflow.WorkflowInstances i         ON i.Id = bs.InstanceId
            INNER JOIN workflow.WorkflowParallelBranches pb ON pb.Id = bs.ParallelBranchId
            WHERE bs.Status = 'InProgress'
              AND i.CompletedAt IS NULL
              AND i.IsDeleted = 0
              AND i.CurrentStateId = pb.BranchingStateId
            """;

        var candidatePublicIds = (await db.QueryAsync<Guid>(sql, cancellationToken: ct)).ToList();
        if (candidatePublicIds.Count == 0)
            return;

        logger.LogInformation(
            "BranchJoinReconcilerJob scanning {Count} candidate stranded branch(es).", candidatePublicIds.Count);

        int completed = 0, transient = 0, permanent = 0, skipped = 0;

        foreach (var instancePublicId in candidatePublicIds)
        {
            // One DI scope (= one DbContext + transaction boundary) per candidate so a failure — or
            // CompleteJoinAsync's change-tracker rollback on failure — can never bleed into the next
            // instance. The completion service opens its own transaction; isolating scopes keeps it contained.
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var branchRepository = sp.GetRequiredService<IParallelBranchRepository>();
            var instanceRepository = sp.GetRequiredService<IWorkflowInstanceRepository>();
            var branchService = sp.GetRequiredService<IParallelBranchService>();
            var joinCompletion = sp.GetRequiredService<IBranchJoinCompletionService>();

            try
            {
                var outcome = await ReconcileOneAsync(
                    instancePublicId, branchRepository, instanceRepository, branchService, joinCompletion, ct);

                switch (outcome)
                {
                    case ReconcileOutcome.Completed: completed++; break;
                    case ReconcileOutcome.Transient: transient++; break;
                    case ReconcileOutcome.Permanent: permanent++; break;
                    default: skipped++; break;
                }
            }
            catch (Exception ex)
            {
                // Per-candidate isolation: one bad strand must not abort the scan for the rest.
                skipped++;
                logger.LogWarning(ex,
                    "BranchJoinReconcilerJob failed reconciling instance {InstanceId}; continuing.", instancePublicId);
            }
        }

        logger.LogInformation(
            "BranchJoinReconcilerJob done: {Completed} joined, {Transient} transient (retry next cycle), "
            + "{Permanent} permanent config strand(s), {Skipped} skipped.",
            completed, transient, permanent, skipped);
    }

    private async Task<ReconcileOutcome> ReconcileOneAsync(
        Guid instancePublicId,
        IParallelBranchRepository branchRepository,
        IWorkflowInstanceRepository instanceRepository,
        IParallelBranchService branchService,
        IBranchJoinCompletionService joinCompletion,
        CancellationToken ct)
    {
        // Re-load under the fresh scope; the scan snapshot may be stale (the synchronous path may have just
        // finished this join). Null / no-longer-InProgress ⇒ already handled ⇒ skip.
        var branchStatus = await branchRepository.GetInstanceBranchStatusAsync(instancePublicId, ct);
        if (branchStatus is null)
            return ReconcileOutcome.Skipped;

        var instanceResult = await instanceRepository.GetWithHistoryAsync(instancePublicId, ct);
        if (instanceResult.IsFailed)
            return ReconcileOutcome.Skipped;
        var instance = instanceResult.Value;

        // The branch configured for the state the instance is parked at. The narrow predicate guarantees
        // CurrentStateId == BranchingStateId, so this resolves the same branch the status belongs to — the
        // Id equality guard rejects any mismatch (e.g. a re-entered branching state from a different branch).
        var definition = await branchRepository.GetBranchByStateIdAsync(
            instance.DefinitionId, instance.CurrentStateId, ct);
        if (definition is null || definition.Id != branchStatus.ParallelBranchId)
            return ReconcileOutcome.Skipped;

        // Full join-condition re-check in memory (the SQL predicate only proved InProgress + parked).
        if (!branchService.CheckJoinCondition(branchStatus, definition))
            return ReconcileOutcome.Skipped;

        var result = await joinCompletion.CompleteJoinAsync(
            instance, definition.ContinuationStateId, SystemActorId, ct);

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "BranchJoinReconcilerJob completed stranded join for instance {InstanceId} → continuation state {StateId}.",
                instancePublicId, definition.ContinuationStateId);
            return ReconcileOutcome.Completed;
        }

        // A handler-less / empty continuation is a config error that fails identically every cycle; throttle it.
        bool isPermanent = result.Errors.Any(e =>
            e.Metadata.TryGetValue("WorkflowErrorCode", out var code)
            && (code as string) == WorkflowErrorCodes.NoHandlerResolved);

        if (isPermanent)
        {
            LogPermanentStrandThrottled(instancePublicId, branchStatus.Id, definition.ContinuationStateId);
            return ReconcileOutcome.Permanent;
        }

        // Anything else (transient concurrency conflict / state-guard) is expected to clear on a later cycle.
        logger.LogWarning(
            "BranchJoinReconcilerJob transient failure completing join for instance {InstanceId}: {Errors}. Retry next cycle.",
            instancePublicId, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ReconcileOutcome.Transient;
    }

    private void LogPermanentStrandThrottled(Guid instancePublicId, long branchStatusId, long continuationStateId)
    {
        var key = $"branch-join-reconciler:permanent-strand:{branchStatusId}";
        if (cache.TryGetValue(key, out _))
            return; // already logged within the window — do not spam the 5-minute cadence.

        // Size is mandatory: the shared IMemoryCache is registered with a SizeLimit (Cache:L1:MaxEntries),
        // so an entry without Size throws "must specify a value for Size when SizeLimit is set".
        cache.Set(key, true, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = PermanentStrandLogWindow,
        });
        logger.LogError(
            "BranchJoinReconcilerJob: instance {InstanceId} branch status {BranchStatusId} has a satisfied join "
            + "but its continuation state {StateId} resolves to no eligible handler (config error). The join "
            + "cannot be completed automatically — fix the definition's continuation assignment. "
            + "Throttled for {Window}.",
            instancePublicId, branchStatusId, continuationStateId, PermanentStrandLogWindow);
    }
}
