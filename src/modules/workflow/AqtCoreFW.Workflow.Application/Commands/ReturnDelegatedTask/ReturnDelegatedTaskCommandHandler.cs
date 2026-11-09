using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ReturnDelegatedTask;

/// <summary>
/// Returns a delegated task to its original assignee.
/// F08 guard: if task is not delegated (DelegatedFromUserId == null), returns success silently.
/// Creates TaskAssignment(Type=Return) audit record only when actually returning.
/// UoW: AddAsync tracks; UpdateAsync flushes both atomically.
///
/// Authorization: actor must be the current task assignee (delegate) OR have IsAdminOverride = true.
/// Tenant isolation: GetByIdAndTenantAsync enforces tenant boundary (C3).
/// Phase-07 PR-D2: injected IWorkflowInstanceRepository to resolve WorkflowInstance.PublicId for DTO.
/// </summary>
public sealed class ReturnDelegatedTaskCommandHandler(
    IWorkflowTaskRepository repository,
    ITaskAssignmentRepository assignmentRepository,
    IWorkflowInstanceRepository instanceRepository)
    : IRequestHandler<ReturnDelegatedTaskCommand, Result<WorkflowTaskDto>>
{
    public async Task<Result<WorkflowTaskDto>> Handle(
        ReturnDelegatedTaskCommand request,
        CancellationToken cancellationToken)
    {
        // C3: tenant-scoped lookup prevents cross-tenant access
        var findResult = await repository.GetByIdAndTenantAsync(request.TaskId, request.TenantId, cancellationToken);
        if (findResult.IsFailed)
            return Result.Fail(new NotFoundError($"WorkflowTask '{request.TaskId}' not found."));

        var task = findResult.Value;

        // Phase-07 PR-D2 C1/M1/M3: lightweight projection — orphan FK = invariant violation → fail loudly.
        // Resolved once here; used in both F08 no-op path and the actual-return path below.
        var instancePublicId = await instanceRepository.GetPublicIdByInternalIdAsync(
            task.WorkflowInstanceId, cancellationToken);
        if (instancePublicId is null)
            return Result.Fail(new NotFoundError(
                $"WorkflowInstance internal Id '{task.WorkflowInstanceId}' missing — orphan FK invariant violation."));

        // F08 guard: no-op if task is not currently delegated
        if (!task.DelegatedFromUserId.HasValue)
            return Result.Ok(CreateWorkflowTaskCommandHandler.MapToDto(task, instancePublicId.Value));

        // C4: runtime authz — actor must be current assignee (the delegate) or have admin override
        if (task.AssigneeId != request.ActorId && !request.IsAdminOverride)
            return Result.Fail(new ForbiddenError(
                "Only the current task holder or an admin may return this delegation."));

        var delegateUserId = task.AssigneeId ?? request.ActorId;
        task.ReturnDelegation(request.ActorId);

        var assignment = TaskAssignment.Create(
            task.Id,
            request.ActorId,
            delegateUserId,
            TaskAssignmentType.Return,
            "delegation-returned");

        await assignmentRepository.AddAsync(assignment, cancellationToken);
        await repository.UpdateAsync(task, cancellationToken);

        return Result.Ok(CreateWorkflowTaskCommandHandler.MapToDto(task, instancePublicId.Value));
    }
}
