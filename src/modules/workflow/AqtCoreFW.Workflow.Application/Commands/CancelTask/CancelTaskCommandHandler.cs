using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Application.Commands.CancelTask;

public sealed class CancelTaskCommandHandler(
    IWorkflowTaskRepository repository,
    IParallelBranchRepository branchRepository,
    IParallelBranchService branchService,
    IWorkflowInstanceRepository instanceRepository,
    IBranchJoinCompletionService joinCompletion,
    ILogger<CancelTaskCommandHandler> logger)
    : IRequestHandler<CancelTaskCommand, Result<WorkflowTaskDto>>
{
    public async Task<Result<WorkflowTaskDto>> Handle(
        CancelTaskCommand request,
        CancellationToken cancellationToken)
    {
        // TaskId is long (T1 entity PK)
        var result = await repository.GetByIdAsync(request.TaskId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(new NotFoundError($"WorkflowTask '{request.TaskId}' not found."));

        var task = result.Value;
        task.Cancel(request.Reason, request.CancelledBy);
        await repository.UpdateAsync(task, cancellationToken);

        // Auto-resolve linked branch child as Rejected on task cancellation (non-fatal)
        if (task.BranchChildStatusId.HasValue)
        {
            try
            {
                // BranchChildStatusId is long (T1 WorkflowBranchChildStatus.Id)
                var branchStatus = await branchRepository.GetBranchStatusByChildStatusIdAsync(
                    task.BranchChildStatusId.Value, cancellationToken);
                if (branchStatus is not null)
                {
                    var childStatus = branchStatus.ChildStatuses
                        .FirstOrDefault(cs => cs.Id == task.BranchChildStatusId.Value);

                    if (childStatus is not null && !childStatus.ResolutionType.HasValue)
                    {
                        // childStatus.BranchChildId is long (T1 WorkflowBranchChild.Id)
                        var resolveResult = await branchService.ResolveBranchChildAsync(
                            branchStatus, childStatus.BranchChildId,
                            BranchResolutionType.Rejected, request.CancelledBy, request.Reason, cancellationToken);

                        if (resolveResult.IsSuccess)
                        {
                            var (joinComplete, continuationStateId) = resolveResult.Value;
                            if (joinComplete && continuationStateId.HasValue)
                            {
                                // branchStatus.InstanceId is long — use internal-Id overload
                                var instanceResult = await instanceRepository.GetWithHistoryByInternalIdAsync(
                                    branchStatus.InstanceId, cancellationToken);
                                if (instanceResult.IsSuccess)
                                {
                                    // Contract: the user's task-cancel already succeeded; a join-completion
                                    // failure must NOT fail it. Surface LOUDLY for an async reconciler
                                    // rather than silently dropping the strand. Shared path also adds the
                                    // continuation pre-check this auto-resolve path previously lacked.
                                    var joinResult = await joinCompletion.CompleteJoinAsync(
                                        instanceResult.Value, continuationStateId.Value,
                                        request.CancelledBy, cancellationToken);
                                    if (joinResult.IsFailed)
                                        logger.LogError(
                                            "Branch join completion failed after auto-resolving (reject) child "
                                            + "of cancelled task on instance {InstanceId}; instance left at "
                                            + "branching state pending reconciliation. Errors: {Errors}",
                                            branchStatus.InstanceId,
                                            string.Join("; ", joinResult.Errors.Select(e => e.Message)));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Auto-resolve failed for cancelled task {TaskId} → branch child {ChildStatusId}.",
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
}
