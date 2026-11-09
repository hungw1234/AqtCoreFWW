using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Orchestrates parallel branching: fan-out creation and join-condition evaluation.
/// </summary>
public interface IParallelBranchService
{
    /// <summary>
    /// STAGES a WorkflowInstanceBranchStatus and one child status per branch child,
    /// plus a WorkflowTask per child for parallel approval tracking, into the change
    /// tracker. Transitions status to InProgress and returns the tracking record.
    /// Does NOT call SaveChangesAsync — the caller owns the single persistence point
    /// via the outer ETR transaction's <c>IWorkflowInstanceRepository.SaveChangesAsync</c>
    /// call (P3 single-tx model; Slice 7 completes the handoff from P2b).
    /// Application cannot touch the DbContext directly (Application→Infrastructure boundary).
    /// Phase-4b B4c: instancePublicId = WorkflowInstance.PublicId (Guid AK).
    ///
    /// <paramref name="childPools"/> carries each branch child's resolved handler pool (keyed by
    /// <c>WorkflowBranchChild.Id</c>), produced by the caller's fork-entry gate. When a child's
    /// resolution is a non-empty predicate pool, the fan-out snapshots it into the child task's
    /// <c>CandidateRefsJson</c> + one <c>WorkflowTaskCandidateIndex</c> row per predicate (same
    /// SaveChanges, S7) so the task is claimable / inbox-visible. The pool takes priority over a
    /// child's <c>DefaultAssigneeId</c>; the default-assignee path applies only when no pool exists.
    /// <paramref name="createdBy"/> is the acting user (audit + claim attribution), not Guid.Empty.
    /// </summary>
    Task<WorkflowInstanceBranchStatus> InitiateBranchingAsync(
        Guid instancePublicId,
        WorkflowParallelBranch branch,
        IReadOnlyDictionary<long, HandlerResolution> childPools,
        Guid tenantId,
        Guid createdBy,
        CancellationToken ct);

    /// <summary>
    /// Records a resolution for a specific child and evaluates the join condition.
    /// Returns Ok((joinComplete, continuationStateId)) on success.
    /// Returns Fail(ConflictError) on DbUpdateConcurrencyException (concurrent resolution).
    /// Phase-4b B4c: childId is long (T1 WorkflowBranchChild.Id — internal bigint PK).
    ///   continuationStateId is long? (T1 WorkflowState.Id — internal bigint PK).
    /// </summary>
    Task<Result<(bool joinComplete, long? continuationStateId)>> ResolveBranchChildAsync(
        WorkflowInstanceBranchStatus branchStatus,
        long childId,
        BranchResolutionType resolution,
        Guid resolvedBy,
        string? comment,
        CancellationToken ct);

    /// <summary>
    /// Evaluates whether the join condition is met given current child statuses.
    /// Pure — does not mutate state.
    /// </summary>
    bool CheckJoinCondition(
        WorkflowInstanceBranchStatus branchStatus,
        WorkflowParallelBranch definition);
}
