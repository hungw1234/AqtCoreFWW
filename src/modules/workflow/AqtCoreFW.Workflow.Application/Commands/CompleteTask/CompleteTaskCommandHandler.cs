using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Application.Commands.CompleteTask;

public sealed class CompleteTaskCommandHandler(
    IWorkflowTaskRepository repository,
    IParallelBranchRepository branchRepository,
    IParallelBranchService branchService,
    IWorkflowInstanceRepository instanceRepository,
    IBranchJoinCompletionService joinCompletion,
    ILogger<CompleteTaskCommandHandler> logger)
    : IRequestHandler<CompleteTaskCommand, Result<WorkflowTaskDto>>
{
    public async Task<Result<WorkflowTaskDto>> Handle(
        CompleteTaskCommand request,
        CancellationToken cancellationToken)
    {
        // TaskId is long (T1 entity PK)
        var result = await repository.GetByIdAsync(request.TaskId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(new NotFoundError($"WorkflowTask '{request.TaskId}' not found."));

        var task = result.Value;
        task.Complete(request.CompletedBy);
        await repository.UpdateAsync(task, cancellationToken);

        // Auto-resolve linked branch child on task completion (non-fatal)
        if (task.BranchChildStatusId.HasValue)
        {
            try
            {
                await AutoResolveBranchChildAsync(
                    task.BranchChildStatusId.Value, BranchResolutionType.Approved,
                    request.CompletedBy, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Auto-resolve failed for task {TaskId} → branch child {ChildStatusId}.",
                    task.Id, task.BranchChildStatusId);
            }
        }

        // Phase-07 PR-D2 C1/M1/M3: lightweight projection — orphan FK = invariant violation → fail loudly.
        var instancePublicId = await instanceRepository.GetPublicIdByInternalIdAsync(
            task.WorkflowInstanceId, cancellationToken);
        if (instancePublicId is null)
            return Result.Fail(new NotFoundError(
                $"WorkflowInstance internal Id '{task.WorkflowInstanceId}' missing — orphan FK invariant violation."));

        return Result.Ok(CreateWorkflowTaskCommandHandler.MapToDto(task, instancePublicId.Value));
    }

    private async Task AutoResolveBranchChildAsync(
        long childStatusId, BranchResolutionType resolution,
        Guid actorId, CancellationToken ct)
    {
        // childStatusId is long (T1 WorkflowBranchChildStatus.Id)
        var branchStatus = await branchRepository.GetBranchStatusByChildStatusIdAsync(childStatusId, ct);
        if (branchStatus is null) return;

        // childStatus.Id is long (T1 WorkflowBranchChildStatus.Id)
        var childStatus = branchStatus.ChildStatuses.FirstOrDefault(cs => cs.Id == childStatusId);
        if (childStatus is null || childStatus.ResolutionType.HasValue) return;

        // childStatus.BranchChildId is long (T1 WorkflowBranchChild.Id)
        var resolveResult = await branchService.ResolveBranchChildAsync(
            branchStatus, childStatus.BranchChildId, resolution, actorId, null, ct);
        if (resolveResult.IsFailed) return;

        var (joinComplete, continuationStateId) = resolveResult.Value;
        if (!joinComplete || !continuationStateId.HasValue)
            return;

        // branchStatus.InstanceId is long — use internal-Id overload
        var instanceResult = await instanceRepository.GetWithHistoryByInternalIdAsync(
            branchStatus.InstanceId, ct);
        if (instanceResult.IsFailed)
            return;

        // Contract: the user's task-complete already succeeded (persisted at Handle()); a join-completion
        // failure here (e.g. a handler-less continuation config error) must NOT fail their action. Surface
        // it LOUDLY so an async reconciler can finish the stranded join — never silently drop it (the old
        // path ignored UpdateWithStateGuardAsync's result, hiding the strand). Shared path also adds the
        // continuation pre-check this auto-resolve path previously lacked.
        var joinResult = await joinCompletion.CompleteJoinAsync(
            instanceResult.Value, continuationStateId.Value, actorId, ct);
        if (joinResult.IsFailed)
            logger.LogError(
                "Branch join completion failed after auto-resolving child of completed task on instance "
                + "{InstanceId}; instance left at branching state pending reconciliation. Errors: {Errors}",
                branchStatus.InstanceId,
                string.Join("; ", joinResult.Errors.Select(e => e.Message)));
    }
}
