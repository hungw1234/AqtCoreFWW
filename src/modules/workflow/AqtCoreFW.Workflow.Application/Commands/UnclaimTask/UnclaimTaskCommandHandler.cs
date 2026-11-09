using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.UnclaimTask;

/// <summary>
/// Current assignee releases a claimed pool task back to the pool (idempotent).
///
/// Assignee guard: caller MUST be the current AssigneeId — otherwise ForbiddenError (403).
/// This is a self-service action; only the holder can release their own claim.
/// Admins wishing to force-release use the /reassign endpoint.
///
/// Idempotency: when AssigneeId is already null (unclaimed), domain Unclaim() is a no-op.
/// Handler detects this by snapshotting pre-state before calling Unclaim().
/// When idempotent: no audit row is written (F5 anti-tautology: audit count unchanged).
///
/// UoW: assignmentRepository.AddAsync (tracks, no save) + repository.UpdateAsync (flushes both).
/// P4 Slice 4-B.
/// </summary>
public sealed class UnclaimTaskCommandHandler(
    IWorkflowTaskRepository repository,
    ITaskAssignmentRepository assignmentRepository,
    IWorkflowInstanceRepository instanceRepository)
    : IRequestHandler<UnclaimTaskCommand, Result<WorkflowTaskDto>>
{
    public async Task<Result<WorkflowTaskDto>> Handle(
        UnclaimTaskCommand request,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped fetch — NotFoundError (404) if missing or cross-tenant.
        var fetchResult = await repository.GetByIdAndTenantAsync(
            request.TaskId, request.TenantId, cancellationToken);
        if (fetchResult.IsFailed)
            return Result.Fail(fetchResult.Errors);

        var task = fetchResult.Value;

        // Assignee guard: only the current holder may unclaim their own task.
        // AssigneeId null (unclaimed) also passes — idempotent path handles below.
        if (task.AssigneeId.HasValue && task.AssigneeId != request.ActorId)
            return Result.Fail(new ForbiddenError(
                $"User '{request.ActorId}' is not the current assignee of task '{request.TaskId}' — cannot unclaim."));

        // Snapshot pre-state to detect idempotent no-op BEFORE domain mutation.
        bool wasUnclaimed = !task.AssigneeId.HasValue;

        // Domain Unclaim() is idempotent: early returns when AssigneeId is already null.
        task.Unclaim(request.ActorId);

        // Idempotent path: task was already unclaimed — no state change, no audit row (F5 invariant).
        if (wasUnclaimed)
        {
            // Resolve instance public id for DTO even on idempotent path (200 with current state).
            var idempotentPublicId = await instanceRepository.GetPublicIdByInternalIdAsync(
                task.WorkflowInstanceId, cancellationToken);
            if (idempotentPublicId is null)
                return Result.Fail(new NotFoundError(
                    $"WorkflowInstance internal Id '{task.WorkflowInstanceId}' missing — orphan FK invariant violation."));

            return Result.Ok(CreateWorkflowTaskCommandHandler.MapToDto(task, idempotentPublicId.Value));
        }

        // Non-idempotent path: task was claimed, now released. Write audit row atomically.
        var assignment = TaskAssignment.Create(
            task.Id,
            request.ActorId,
            request.ActorId,
            TaskAssignmentType.Unclaim);

        await assignmentRepository.AddAsync(assignment, cancellationToken);

        // UpdateAsync flushes task mutation + audit row in one SaveChangesAsync call.
        await repository.UpdateAsync(task, cancellationToken);

        var instancePublicId = await instanceRepository.GetPublicIdByInternalIdAsync(
            task.WorkflowInstanceId, cancellationToken);
        if (instancePublicId is null)
            return Result.Fail(new NotFoundError(
                $"WorkflowInstance internal Id '{task.WorkflowInstanceId}' missing — orphan FK invariant violation."));

        return Result.Ok(CreateWorkflowTaskCommandHandler.MapToDto(task, instancePublicId.Value));
    }
}
