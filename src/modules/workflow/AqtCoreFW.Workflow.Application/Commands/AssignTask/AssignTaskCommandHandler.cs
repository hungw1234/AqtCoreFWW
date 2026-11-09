using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.AssignTask;

/// <summary>
/// Assigns a workflow task to a user and records the assignment history.
/// F09 fix: TaskAssignment now persisted via ITaskAssignmentRepository.AddAsync.
/// UoW (Step 0c): AddAsync tracks without saving; repository.UpdateAsync flushes
/// both task mutation and assignment in one SaveChangesAsync — atomic.
/// Phase-07 PR-D2: injected IWorkflowInstanceRepository to resolve WorkflowInstance.PublicId for DTO.
/// </summary>
public sealed class AssignTaskCommandHandler(
    IWorkflowTaskRepository repository,
    ITaskAssignmentRepository assignmentRepository,
    IWorkflowInstanceRepository instanceRepository)
    : IRequestHandler<AssignTaskCommand, Result<WorkflowTaskDto>>
{
    public async Task<Result<WorkflowTaskDto>> Handle(
        AssignTaskCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(request.TaskId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(new NotFoundError($"WorkflowTask '{request.TaskId}' not found."));

        var task = result.Value;

        // Branch-child tasks are pool-claim only: they carry a per-child assignment pool and
        // are taken via self-service Claim (pool-eligibility re-validated there). Manual assign
        // would bypass that eligibility gate, so it is rejected for any caller (incl. admin).
        // Automated fan-out (ParallelBranchService) and escalation (EscalationCheckJob) call the
        // domain task.Assign() directly and are intentionally unaffected by this command guard.
        if (task.BranchChildStatusId is not null)
            return Result.Fail(new ValidationError(
                "Branch-child tasks are claimed via the pool and cannot be manually assigned."));

        task.Assign(request.AssigneeId, request.AssignedById);

        // Track assignment in EF change tracker — no save yet
        var assignment = TaskAssignment.Create(
            task.Id,
            request.AssignedById,
            request.AssigneeId,
            TaskAssignmentType.Initial,
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
