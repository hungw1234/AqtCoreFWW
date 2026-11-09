using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Default implementation of <see cref="IWorkflowTaskFactory"/>.
///
/// Body is verbatim lift of <c>ExecuteTransitionCommandHandler:316-338</c> (DSEAM-a, Slice 10a)
/// with no logic drift. DRY foundation for Phase 5b (instance-creation first-task) and beyond.
///
/// <b>S7 single-SaveChanges invariant:</b> never calls <c>SaveChangesAsync</c>.
/// Stages via <see cref="IWorkflowTaskRepository.Stage"/> only; caller flushes.
///
/// <b>Architecture rule:</b> pure Application layer — no Infrastructure dependencies.
/// Mirrors <see cref="IWorkflowHandlerResolver"/> shape.
/// </summary>
internal sealed class WorkflowTaskFactory(IWorkflowTaskRepository taskRepository) : IWorkflowTaskFactory
{
    /// <inheritdoc/>
    public WorkflowTask StageFirstOrNextTask(
        long instanceInternalId,
        string title,
        Guid tenantId,
        Guid createdBy,
        Guid? overrideAssignee,
        HandlerResolution resolution)
    {
        Guid? finalAssignee = ResolveFinalAssignee(overrideAssignee, resolution);

        var task = WorkflowTask.Create(
            workflowInstanceId: instanceInternalId,
            title: title,
            description: null,
            assigneeId: finalAssignee,
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,                            // Phase-5 owns SLA/due-date wiring
            tenantId: tenantId,
            createdBy: createdBy);

        ApplyCandidateSnapshot(task, finalAssignee, resolution, tenantId, taskRepository);
        taskRepository.Stage(task);
        return task;
    }

    /// <inheritdoc/>
    public WorkflowTask StageFirstOrNextTask(
        WorkflowInstance instance,
        string title,
        Guid tenantId,
        Guid createdBy,
        Guid? overrideAssignee,
        HandlerResolution resolution)
    {
        Guid? finalAssignee = ResolveFinalAssignee(overrideAssignee, resolution);

        // Instance-creation overload: pass the entity so EF nav-prop propagates the
        // IDENTITY-generated instance.Id to task.WorkflowInstanceId in a single SaveChanges.
        var task = WorkflowTask.Create(
            instance: instance,
            title: title,
            description: null,
            assigneeId: finalAssignee,
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: createdBy);

        ApplyCandidateSnapshot(task, finalAssignee, resolution, tenantId, taskRepository);
        taskRepository.Stage(task);
        return task;
    }

    // DASSIGN-a: if override accepted (set by DLIVE-b gate), use override;
    // otherwise use resolution (single Assignee OR pool).
    private static Guid? ResolveFinalAssignee(Guid? overrideAssignee, HandlerResolution resolution) =>
        overrideAssignee ?? resolution.Assignee;

    // For pool tasks where override did NOT fire: snapshot the candidate predicates as JSON
    // (CandidateRefsJson — stable audit/equality value) AND materialize one
    // WorkflowTaskCandidateIndex row per predicate for the inbox/claim read-side queries.
    // Both writes derive from the same predicate list in one call so they cannot drift.
    // Nav-prop WorkflowTask is set (not WorkflowTaskId) so EF relationship-fixup propagates
    // the IDENTITY-generated task.Id to each index row's FK at SaveChanges time (S7 invariant).
    private static void ApplyCandidateSnapshot(
        WorkflowTask task,
        Guid? finalAssignee,
        HandlerResolution resolution,
        Guid tenantId,
        IWorkflowTaskRepository taskRepository)
    {
        if (finalAssignee is not null || resolution.CandidatePredicates.Count == 0)
            return;

        task.SetCandidate(CandidatePrincipalSerializer.Serialize(resolution.CandidatePredicates));

        var rows = new List<WorkflowTaskCandidateIndex>(resolution.CandidatePredicates.Count);
        foreach (var p in resolution.CandidatePredicates)
            rows.Add(WorkflowTaskCandidateIndex.CreateForStaging(
                task: task,
                clauseId: 0,
                combineMode: p.Combine,
                principalType: p.Type,
                principalRefId: p.Ref,
                tenantId: tenantId));

        taskRepository.StageCandidateIndex(rows);
    }
}
