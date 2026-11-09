using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Write-side repository for WorkflowTask aggregate.
/// Phase-4b B4c: T1 entity — IRepository&lt;T, long&gt; key is internal bigint PK.
/// </summary>
public interface IWorkflowTaskRepository : IRepository<WorkflowTask, long>
{
    /// <summary>Returns all tasks for a given workflow instance (by internal long Id).</summary>
    Task<IReadOnlyList<WorkflowTask>> GetByInstanceAsync(
        long instanceInternalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the instance's currently-actionable, non-branch tasks (Status Open or InProgress,
    /// BranchChildStatusId null) as TRACKED entities, so the caller can mutate and flush them within
    /// its own SaveChanges. Used by <c>ExecuteTransitionCommandHandler</c> to complete the source-state
    /// work item when a transition advances the instance. Branch-child tasks are excluded — those are
    /// owned by the parallel-branch lifecycle. Filtered in SQL (unlike <see cref="GetByInstanceAsync"/>)
    /// so a long task history does not bloat the change-tracker on every transition.
    /// </summary>
    Task<IReadOnlyList<WorkflowTask>> GetOpenNonBranchByInstanceAsync(
        long instanceInternalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most-recently completed task for <paramref name="instanceInternalId"/>,
    /// or <c>null</c> when no prior completed task exists. Ordered by <c>CompletedAt DESC</c>;
    /// uses <c>AsNoTracking</c> (read-only producer support for the <c>PreviousHandler</c>
    /// assignment-clause mode resolution, RT2-15 Phase 01).
    /// </summary>
    Task<WorkflowTask?> GetLastCompletedTaskAsync(
        long instanceInternalId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns open/in-progress tasks assigned to a specific user within a tenant.</summary>
    Task<IReadOnlyList<WorkflowTask>> GetByAssigneeAsync(
        Guid assigneeId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the active task linked to a branch child status (for assignee authorization).</summary>
    Task<WorkflowTask?> GetByBranchChildStatusIdAsync(
        long branchChildStatusInternalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tenant-scoped lookup: returns the task only if it belongs to the given tenant.
    /// Uses FirstOrDefaultAsync (respects global query filters) + explicit TenantId check.
    /// Returns NotFoundError if the task does not exist or belongs to a different tenant.
    /// </summary>
    Task<Result<WorkflowTask>> GetByIdAndTenantAsync(
        long id,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages <paramref name="task"/> in the EF ChangeTracker WITHOUT calling
    /// <c>SaveChangesAsync</c>. Used by <c>ExecuteTransitionCommandHandler</c> to enlist
    /// the new task in the same single-SaveChanges boundary as the WorkflowInstance state
    /// update (S7 single-SaveChanges invariant). Caller MUST invoke
    /// <c>IWorkflowInstanceRepository.SaveChangesAsync</c> to flush.
    /// Slice 10a: task-creation seam (DSEAM-a).
    /// </summary>
    void Stage(WorkflowTask task);

    /// <summary>
    /// Stages <paramref name="rows"/> in the EF ChangeTracker WITHOUT calling
    /// <c>SaveChangesAsync</c>. Each row must have been created via
    /// <see cref="WorkflowTaskCandidateIndex.CreateForStaging"/> with its
    /// <c>WorkflowTask</c> nav-prop set to the parent task staged via <see cref="Stage"/>.
    /// EF relationship-fixup propagates the DB IDENTITY-generated parent Id to the FK column
    /// at SaveChanges time (S7 single-SaveChanges invariant preserved).
    /// </summary>
    void StageCandidateIndex(IReadOnlyList<WorkflowTaskCandidateIndex> rows);

    /// <summary>
    /// Returns true when the caller's <paramref name="permissionCodes"/> intersect at least one
    /// <c>WorkflowTaskCandidateIndex</c> row with <c>PrincipalType=Permission</c>, OR
    /// <paramref name="roleCodes"/> intersect at least one row with <c>PrincipalType=Role</c>,
    /// OR <paramref name="groupCodes"/> intersect at least one row with <c>PrincipalType=Group</c>,
    /// OR <paramref name="orgUnitCodes"/> intersect at least one row with <c>PrincipalType=OrgUnit</c>
    /// (all predicates ANDed with <c>WorkflowTaskId = taskId</c> AND <c>TenantId = tenantId</c>).
    /// PrincipalType ∈ {Permission, Role, Group, OrgUnit}.
    /// Re-validates inside ClaimTaskCommandHandler to close the pre-check→Claim TOCTOU window
    /// (identical predicate to inbox query per plan spec Q2).
    /// P4 Slice 4-B; Role support added by T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION;
    /// Group support added by T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP;
    /// OrgUnit support added by RT2-15 Phase 04.
    /// </summary>
    Task<bool> IsPoolEligibleAsync(
        long taskId,
        IReadOnlySet<string> permissionCodes,
        IReadOnlySet<string> roleCodes,
        IReadOnlySet<string> groupCodes,
        IReadOnlySet<string> orgUnitCodes,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a mutated <see cref="WorkflowTask"/> that has already been mutated in-memory
    /// (e.g. after <c>Claim()</c>), flushing both the task and any pending audit rows tracked
    /// in the same EF change-tracker scope.
    /// Catches <c>DbUpdateConcurrencyException</c> at the Infrastructure boundary (RowVersion
    /// mismatch from concurrent Claim race) and translates it to either
    /// <c>Result.Fail(WorkflowErrors.TaskFinalState)</c> (Completed/Cancelled) or
    /// <c>Result.Fail(WorkflowErrors.AlreadyClaimed)</c> — no EF types cross to Application.
    /// P4 Slice 4-B. P4 Slice 4-C: M1 exception-discrimination added.
    /// </summary>
    Task<Result> UpdateWithClaimGuardAsync(
        WorkflowTask task,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the caller's inbox: open/in-progress tasks that are either directly assigned to
    /// <paramref name="userId"/> OR belong to a pool the caller can claim — EXISTS on
    /// <c>WorkflowTaskCandidateIndex</c> with either
    /// (<c>PrincipalType=Permission</c> ∩ <paramref name="permissionCodes"/>) OR
    /// (<c>PrincipalType=Role</c> ∩ <paramref name="roleCodes"/>) OR
    /// (<c>PrincipalType=Group</c> ∩ <paramref name="groupCodes"/>) OR
    /// (<c>PrincipalType=OrgUnit</c> ∩ <paramref name="orgUnitCodes"/>).
    /// DCAND-a payoff: uses EF EXISTS subquery over the nav-prop FK (not JSON-read).
    /// Projects directly to <see cref="WorkflowTaskDto"/> — no entity materialization.
    /// WorkflowInstanceId is Guid (T2 PublicId via JOIN to WorkflowInstances).
    /// PrincipalType ∈ {Permission, Role, Group, OrgUnit}.
    /// P4 Slice 4-C; Role support added by T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION;
    /// Group support added by T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP;
    /// OrgUnit support added by RT2-15 Phase 04.
    /// </summary>
    Task<IReadOnlyList<WorkflowTaskDto>> GetInboxAsync(
        Guid userId,
        IReadOnlySet<string> permissionCodes,
        IReadOnlySet<string> roleCodes,
        IReadOnlySet<string> groupCodes,
        IReadOnlySet<string> orgUnitCodes,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
