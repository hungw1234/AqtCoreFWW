using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ReassignTask;

/// <summary>
/// Reassigns a workflow task to a new assignee and records the assignment history.
///
/// Phase-4-A IDOR fix (stomp guard + audit type branching):
///   - Pre-state snapshot: capture wasAlreadyClaimed BEFORE task.Assign() mutates AssigneeId.
///   - wasAlreadyClaimed=true (admin stomp):
///       * Reason MUST be non-empty → ValidationError (422) if missing.
///       * Audit row uses Type=ReassignAdmin.
///   - wasAlreadyClaimed=false (unclaimed):
///       * Reason optional (backward compatibility).
///       * Audit row uses Type=Reassign.
///
/// F09 fix: TaskAssignment now persisted via ITaskAssignmentRepository.AddAsync.
/// UoW (Step 0c): AddAsync tracks the assignment without saving; repository.UpdateAsync
/// flushes the entire EF change tracker — task mutation + assignment in one SaveChangesAsync.
/// Phase-07 PR-D2: injected IWorkflowInstanceRepository to resolve WorkflowInstance.PublicId for DTO.
/// </summary>
public sealed class ReassignTaskCommandHandler(
    IWorkflowTaskRepository repository,
    ITaskAssignmentRepository assignmentRepository,
    IWorkflowInstanceRepository instanceRepository)
    : IRequestHandler<ReassignTaskCommand, Result<WorkflowTaskDto>>
{
    public async Task<Result<WorkflowTaskDto>> Handle(
        ReassignTaskCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(request.TaskId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(new NotFoundError($"WorkflowTask '{request.TaskId}' not found."));

        var task = result.Value;

        // Branch-child tasks are pool-claim only (see AssignTaskCommandHandler): manual reassign
        // would bypass pool-eligibility. Rejected for any caller; automated paths (fan-out,
        // escalation) call domain task.Assign() directly and are unaffected by this command guard.
        if (task.BranchChildStatusId is not null)
            return Result.Fail(new ValidationError(
                "Branch-child tasks are claimed via the pool and cannot be manually reassigned."));

        // Phase-4-A: snapshot pre-state BEFORE task.Assign() overwrites AssigneeId.
        // This is the stomp-guard decision point: was the task already claimed by someone?
        bool wasAlreadyClaimed = task.AssigneeId.HasValue;

        // Phase-4-A: stomp guard — admin overwriting a claimed task MUST supply a reason.
        // Reason is validated here (not in FluentValidation) because the pre-state (AssigneeId)
        // is only available after a DB read; validators operate on command shape alone.
        if (wasAlreadyClaimed && string.IsNullOrWhiteSpace(request.Reason))
            return Result.Fail(new ValidationError(
                "Reason is required when reassigning an already-assigned task (admin stomp).",
                property: "Reason"));

        task.Assign(request.NewAssigneeId, request.ReassignedById);

        // Phase-4-A: choose audit type based on pre-state snapshot.
        var assignmentType = wasAlreadyClaimed
            ? TaskAssignmentType.ReassignAdmin
            : TaskAssignmentType.Reassign;

        // Track assignment in EF change tracker — no save yet
        var assignment = TaskAssignment.Create(
            task.Id,
            request.ReassignedById,
            request.NewAssigneeId,
            assignmentType,
            request.Reason);

        await assignmentRepository.AddAsync(assignment, cancellationToken);

        // UpdateAsync calls SaveChangesAsync once — flushes both task and assignment atomically
        await repository.UpdateAsync(task, cancellationToken);

        // Phase-07 PR-D2 C1/M1/M3: lightweight projection — orphan FK = invariant violation → fail loudly.
        var instancePublicId = await instanceRepository.GetPublicIdByInternalIdAsync(
            task.WorkflowInstanceId, cancellationToken);
        if (instancePublicId is null)
            return Result.Fail(new NotFoundError(
                $"WorkflowInstance internal Id '{task.WorkflowInstanceId}' missing — orphan FK invariant violation."));

        return Result.Ok(CreateWorkflowTaskCommandHandler.MapToDto(task, instancePublicId.Value));
    }
}
