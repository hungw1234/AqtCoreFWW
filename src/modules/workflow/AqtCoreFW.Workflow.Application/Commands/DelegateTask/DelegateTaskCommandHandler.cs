using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.DelegateTask;

/// <summary>
/// Delegates a task to another user.
/// Records original assignee in DelegatedFromUserId, sets AssigneeId to delegate.
/// Creates TaskAssignment(Type=Delegate) audit record.
/// UoW: AddAsync tracks assignment; UpdateAsync flushes both atomically.
///
/// Authorization: actor must be the current task assignee OR have IsAdminOverride = true.
/// Tenant isolation: GetByIdAndTenantAsync enforces tenant boundary (C3).
/// H5: rejects delegation if task has no current assignee.
/// Phase-07 PR-D2: injected IWorkflowInstanceRepository to resolve WorkflowInstance.PublicId for DTO.
/// </summary>
public sealed class DelegateTaskCommandHandler(
    IWorkflowTaskRepository repository,
    ITaskAssignmentRepository assignmentRepository,
    IWorkflowInstanceRepository instanceRepository)
    : IRequestHandler<DelegateTaskCommand, Result<WorkflowTaskDto>>
{
    public async Task<Result<WorkflowTaskDto>> Handle(
        DelegateTaskCommand request,
        CancellationToken cancellationToken)
    {
        // C3: tenant-scoped lookup prevents cross-tenant access
        var findResult = await repository.GetByIdAndTenantAsync(request.TaskId, request.TenantId, cancellationToken);
        if (findResult.IsFailed)
            return Result.Fail(new NotFoundError($"WorkflowTask '{request.TaskId}' not found."));

        var task = findResult.Value;

        // H5: reject delegation of unassigned tasks — fallback-to-actor creates phantom ownership
        if (task.AssigneeId is null)
            return Result.Fail(new ValidationError("Cannot delegate an unassigned task."));

        // C4: runtime authz — actor must be current assignee or have admin override
        if (task.AssigneeId != request.ActorId && !request.IsAdminOverride)
            return Result.Fail(new ForbiddenError(
                "Only the task assignee or an admin may delegate this task."));

        // Domain method throws if already delegated — convert to ValidationError
        try
        {
            task.Delegate(task.AssigneeId.Value, request.DelegateToUserId, request.ActorId);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(new ValidationError(ex.Message));
        }

        var assignment = TaskAssignment.Create(
            task.Id,
            request.ActorId,
            request.DelegateToUserId,
            TaskAssignmentType.Delegate,
            request.Reason);

        await assignmentRepository.AddAsync(assignment, cancellationToken);
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
