using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Events;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Workflow.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of IParallelBranchService.
/// Slice 7: InitiateBranchingAsync STAGES (no SaveChanges — caller persists via the
/// outer ETR transaction's single SaveChanges; P2b PersistStagedBranchAsync removed).
/// ResolveBranchChildAsync commits the child resolution (so concurrent siblings see it — the
/// reload-after-save concurrency guard, RT3-FM-F4) and DETECTS the join, but no longer marks the
/// branch resolved: MarkResolved + the continuation transition + the continuation task are applied
/// atomically by IBranchJoinCompletionService so the join can never publish before the continuation.
/// Join logic is pure (no I/O).
/// Phase-4b B4c:
///   InitiateBranchingAsync: instancePublicId=Guid resolved → internal long Id for entity creation.
///     WorkflowInstanceBranchStatus.Create(long, long), WorkflowTask.CreateForBranchChild(long, long, …).
///   ResolveBranchChildAsync: childId=long (T1 BranchChild.Id), return (bool, long?) continuationStateId.
///   CancelSiblingsIfNeeded: resolvedChildId=long.
/// </summary>
public sealed class ParallelBranchService(WorkflowDbContext db, IGuidGenerator gen) : IParallelBranchService
{
    /// <inheritdoc/>
    /// <remarks>
    /// instancePublicId is the T2 WorkflowInstance.PublicId (Guid AK).
    /// Resolved to internal long Id so WorkflowInstanceBranchStatus.Create and
    /// WorkflowTask.CreateForBranchChild (which take long workflowInstanceId) are correct.
    /// </remarks>
    public async Task<WorkflowInstanceBranchStatus> InitiateBranchingAsync(
        Guid instancePublicId,
        WorkflowParallelBranch branch,
        IReadOnlyDictionary<long, HandlerResolution> childPools,
        Guid tenantId,
        Guid createdBy,
        CancellationToken ct)
    {
        // Resolve T2 PublicId → internal long Id (needed by entity Create factories)
        var instanceInternalId = await db.Set<WorkflowInstance>()
            .Where(i => i.PublicId == instancePublicId)
            .Select(i => i.Id)
            .FirstAsync(ct);

        var branchStatus = WorkflowInstanceBranchStatus.Create(instanceInternalId, branch.Id);

        // Create a pending child status for each branch child
        foreach (var child in branch.Children)
        {
            var childStatus = WorkflowBranchChildStatus.Create(branchStatus.Id, child.Id);
            branchStatus.AddChildStatus(childStatus);
        }

        branchStatus.MarkInProgress();
        db.InstanceBranchStatuses.Add(branchStatus);

        // Auto-create approval tasks linked to each branch child
        var dueDate = branch.TimeoutMinutes > 0
            ? DateTime.UtcNow.AddMinutes(branch.TimeoutMinutes)
            : (DateTime?)null;

        foreach (var childStatus in branchStatus.ChildStatuses)
        {
            var child = branch.Children.First(c => c.Id == childStatus.BranchChildId);
            var title = !string.IsNullOrWhiteSpace(child.DisplayNameVi) ? child.DisplayNameVi : child.Name;

            // childStatus.Id is still 0 here (IDENTITY not assigned until the caller's SaveChanges). Link
            // via the navigation, not the scalar Id, so EF propagates the generated child-status Id into
            // WorkflowTask.BranchChildStatusId during that single SaveChanges (the FK relationship is
            // configured in WorkflowTaskConfiguration). Passing childStatus.Id here would persist a stale 0
            // and break GetByBranchChildStatusIdAsync (the task↔status link claim/resolve depends on).
            // createdBy is the acting user (audit + claim attribution), not Guid.Empty.
            var task = WorkflowTask.CreateForBranchChild(
                instanceInternalId, childStatus, title, child.Name,
                WorkflowTaskPriority.Normal, dueDate, tenantId, createdBy);

            // Route the child task to its handler. The caller's fork-entry gate already proved every
            // child is resolvable (non-empty pool OR a default assignee), so exactly one arm fires.
            var pool = childPools.TryGetValue(child.Id, out var resolved) ? resolved : HandlerResolution.Empty;
            if (pool.Assignee is { } singleUser)
            {
                // Defensive-only under the current invariant: the graph validator restricts branch-child
                // pools to ByPermission/ByRole, which always resolve to a predicate pool — never a single
                // Assignee. This arm exists to stay correct if that restriction is ever relaxed (e.g.
                // SpecificUser/Initiator child clauses); it is not a live path today.
                task.Assign(singleUser, createdBy);
            }
            else if (pool.CandidatePredicates.Count > 0)
            {
                // Pooled child: snapshot the predicate list into CandidateRefsJson AND materialize one
                // WorkflowTaskCandidateIndex row per predicate (same SaveChanges, S7) so the task is
                // claimable + inbox-visible. Pool takes priority over DefaultAssigneeId — a pooled task
                // is claimed by a member, not pre-locked to one user (which would hide it from the pool
                // and double-surface it). Both writes derive from the same list so they cannot drift.
                task.SetCandidate(CandidatePrincipalSerializer.Serialize(pool.CandidatePredicates));
                foreach (var predicate in pool.CandidatePredicates)
                    db.WorkflowTaskCandidateIndex.Add(WorkflowTaskCandidateIndex.CreateForStaging(
                        task, clauseId: 0, predicate.Combine, predicate.Type, predicate.Ref, tenantId));
            }
            else if (child.DefaultAssigneeId.HasValue)
            {
                // No pool rule configured — the legacy default assignee is the child's only handler.
                task.Assign(child.DefaultAssigneeId.Value, createdBy);
            }

            // Raise domain event for notification handler (dispatched post-SaveChanges by outbox interceptor)
            // TaskInternalId=long (task.Id is 0 pre-save; event dispatched post-save by outbox)
            task.AddDomainEvent(new ParallelApprovalTaskCreatedEvent(
                task.Id, instancePublicId, task.AssigneeId, task.Title, tenantId,
                gen.NewIdMonotonic()));

            db.WorkflowTasks.Add(task);
        }

        // Slice 7 (RT3-H2): STAGE ONLY — no SaveChangesAsync here. Caller owns the
        // single persistence point via the outer ETR transaction's SaveChangesAsync.
        return branchStatus;
    }

    /// <inheritdoc/>
    public async Task<Result<(bool joinComplete, long? continuationStateId)>> ResolveBranchChildAsync(
        WorkflowInstanceBranchStatus branchStatus,
        long childId,
        BranchResolutionType resolution,
        Guid resolvedBy,
        string? comment,
        CancellationToken ct)
    {
        try
        {
            // Load the full branch status with children if not already loaded
            var status = await db.InstanceBranchStatuses
                .Include(s => s.ChildStatuses)
                .FirstOrDefaultAsync(s => s.Id == branchStatus.Id, ct)
                ?? branchStatus;

            // Find and resolve the target child (childId=long — T1 BranchChild.Id FK)
            var childStatus = status.ChildStatuses.FirstOrDefault(cs => cs.BranchChildId == childId)
                ?? throw new InvalidOperationException(
                    $"BranchChildStatus not found for child {childId} in branch status {branchStatus.Id}.");

            childStatus.Resolve(resolution, resolvedBy, comment);

            // Load the branch definition to evaluate join condition
            var definition = await db.ParallelBranches
                .Include(b => b.Children)
                .FirstOrDefaultAsync(b => b.Id == status.ParallelBranchId, ct)
                ?? throw new InvalidOperationException(
                    $"WorkflowParallelBranch {status.ParallelBranchId} not found.");

            // Cancel siblings immediately for OR strategy or reject-cancel
            CancelSiblingsIfNeeded(status, definition, resolution, childId);

            await db.SaveChangesAsync(ct);

            // Reload fresh state from DB — concurrent threads may have resolved other children
            // between our load and save. Without reload, CheckJoinCondition uses stale data
            // and can miss the join when two tasks complete simultaneously.
            // MUST be AsNoTracking: a tracking re-query returns the children already tracked from the
            // load above and discards the DB row values (EF identity-map resolution), so a sibling's
            // cross-context commit would stay invisible and BOTH resolvers would read the join as
            // incomplete — leaving the branch InProgress with every child resolved (a stranded join).
            var freshStatus = await db.InstanceBranchStatuses
                .Include(s => s.ChildStatuses)
                .AsNoTracking()
                .FirstAsync(s => s.Id == status.Id, ct);

            if (!CheckJoinCondition(freshStatus, definition))
                return Result.Ok<(bool, long?)>((false, null));

            // Join condition satisfied. The branch is deliberately NOT marked resolved here: MarkResolved
            // + the continuation transition + the continuation work item are applied together, atomically,
            // by IBranchJoinCompletionService (the branch RowVersion is the winner-election token there).
            // Marking resolved + committing here would publish the join before the continuation lands,
            // re-opening the strand window (resolved branch, instance never advanced).
            // ContinuationStateId: long (T1 WorkflowState.Id — no Guid AK).
            return Result.Ok<(bool, long?)>((true, definition.ContinuationStateId));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail<(bool, long?)>(
                new ConflictError("CONCURRENCY_CONFLICT"));
        }
    }

    /// <inheritdoc/>
    public bool CheckJoinCondition(
        WorkflowInstanceBranchStatus branchStatus,
        WorkflowParallelBranch definition)
    {
        var children = branchStatus.ChildStatuses;
        var resolvedCount = children.Count(cs => cs.ResolutionType.HasValue);
        var approvedCount = children.Count(cs => cs.ResolutionType == BranchResolutionType.Approved);
        var totalCount = children.Count;

        return definition.BranchType switch
        {
            // AND: all resolved AND all approved (or cancel-on-reject short-circuit handled in Resolve)
            ParallelBranchType.And =>
                resolvedCount == totalCount && approvedCount == totalCount,

            // OR: any single child resolved (first resolution wins)
            ParallelBranchType.Or =>
                resolvedCount >= 1,

            // N_OF_M: enough approvals reached the threshold
            ParallelBranchType.NOfM =>
                definition.RequiredCount.HasValue && approvedCount >= definition.RequiredCount.Value,

            _ => false
        };
    }

    // Cancel pending siblings for OR (first-win) or AND+AllowCancelOnAnyReject.
    // resolvedChildId: long (T1 WorkflowBranchChild.Id — BranchChildId FK on ChildStatus).
    private static void CancelSiblingsIfNeeded(
        WorkflowInstanceBranchStatus status,
        WorkflowParallelBranch definition,
        BranchResolutionType resolution,
        long resolvedChildId)
    {
        bool shouldCancel = definition.BranchType == ParallelBranchType.Or
            || (definition.BranchType == ParallelBranchType.And
                && definition.AllowCancelOnAnyReject
                && resolution == BranchResolutionType.Rejected);

        if (!shouldCancel) return;

        foreach (var sibling in status.ChildStatuses
            .Where(cs => cs.BranchChildId != resolvedChildId && !cs.ResolutionType.HasValue))
        {
            sibling.Resolve(BranchResolutionType.Rejected, Guid.Empty, "Cancelled by sibling resolution");
        }
    }
}
