using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Application service that creates and stages a new <see cref="WorkflowTask"/> in the
/// EF ChangeTracker within the caller's single-SaveChanges boundary.
///
/// <para><b>DRY contract:</b> single authoritative source for the task-creation field-mapping
/// logic extracted from <c>ExecuteTransitionCommandHandler:316-338</c> (DSEAM-a, Slice 10a).
/// Phase 5b (instance creation) is the second caller.</para>
///
/// <para><b>Single-SaveChanges invariant (S7):</b> this factory NEVER calls
/// <c>SaveChangesAsync</c>. It stages via <c>IWorkflowTaskRepository.Stage(task)</c>;
/// the caller MUST flush via <c>IWorkflowInstanceRepository.SaveChangesAsync</c>.</para>
///
/// <para><b>Override precedence (DASSIGN-a, Slice 10a):</b> when the <c>overrideAssignee</c>
/// argument is non-null it takes absolute priority — the resolver's
/// <see cref="HandlerResolution.Assignee"/> and <see cref="HandlerResolution.CandidatePredicates"/>
/// are both ignored.</para>
///
/// <para><b>Dumb-pipe invariant (F5):</b> when the resolution is
/// <see cref="HandlerResolution.IsEmpty"/> AND <c>overrideAssignee</c> is null,
/// the factory stages a task with no assignee and no candidate JSON. The caller is
/// responsible for having already rejected <c>WF_NO_HANDLER</c> before reaching this point.</para>
///
/// <para><b>Scope:</b> stages one task for linear (non-branch) transitions. Not for batch
/// or branch tasks — use <c>IParallelBranchService</c> for branch-child tasks.</para>
/// </summary>
public interface IWorkflowTaskFactory
{
    /// <summary>
    /// Creates a <see cref="WorkflowTask"/>, applies candidate snapshot if needed, and
    /// stages it in the EF ChangeTracker.
    /// </summary>
    /// <param name="instanceInternalId">Internal long PK of the workflow instance (T2 FK).</param>
    /// <param name="title">Task title — caller supplies; no policy in factory.</param>
    /// <param name="tenantId">Tenant that owns this task.</param>
    /// <param name="createdBy">Actor who triggered creation (audit trail).</param>
    /// <param name="overrideAssignee">
    /// When non-null, overrides all resolver output (DASSIGN-a). Wins over
    /// <paramref name="resolution"/>.<see cref="HandlerResolution.Assignee"/> and empty pool.
    /// </param>
    /// <param name="resolution">Handler resolution from <c>IWorkflowHandlerResolver</c>.</param>
    /// <returns>The staged <see cref="WorkflowTask"/> (caller may inspect fields).</returns>
    WorkflowTask StageFirstOrNextTask(
        long instanceInternalId,
        string title,
        Guid tenantId,
        Guid createdBy,
        Guid? overrideAssignee,
        HandlerResolution resolution);

    /// <summary>
    /// Instance-creation overload (PHR-V2 Phase 6 S4 / GATE-1 Path A).
    ///
    /// Accepts the staged-but-unsaved <see cref="WorkflowInstance"/> entity and sets the EF
    /// navigation property so a single <c>SaveChangesAsync</c> can persist both rows atomically:
    /// the IDENTITY-generated <c>instance.Id</c> is propagated to <c>task.WorkflowInstanceId</c>
    /// by EF between the INSERTs — no explicit transaction or two-step save required.
    ///
    /// Use ONLY when the instance is staged in the same change-tracker scope and not yet
    /// flushed. For existing-instance task creation (ExecuteTransition, branch fan-out) use the
    /// <c>StageFirstOrNextTask(long instanceInternalId, …)</c> overload.
    /// </summary>
    WorkflowTask StageFirstOrNextTask(
        WorkflowInstance instance,
        string title,
        Guid tenantId,
        Guid createdBy,
        Guid? overrideAssignee,
        HandlerResolution resolution);
}
