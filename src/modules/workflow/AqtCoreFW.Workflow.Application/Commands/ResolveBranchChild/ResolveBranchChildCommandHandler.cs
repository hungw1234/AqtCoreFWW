using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ResolveBranchChild;

/// <summary>
/// Resolves a branch child with tenant isolation (F1), assignee authorization (F2),
/// and concurrency conflict handling (F4).
/// Flow:
///   1. Load instance by PublicId → verify tenant ownership
///   2. Load branch status → find target child (long Id)
///   3. Load linked task → verify assignee == request.ResolvedBy
///   4. Delegate resolution to IParallelBranchService (returns Result for concurrency)
///   5. If join complete → apply transition to continuationStateId (long T1 state PK)
/// Phase-4b B4c: ChildStatusId = long (T1); InstanceId = Guid (T2 PublicId).
/// </summary>
public sealed class ResolveBranchChildCommandHandler(
    IParallelBranchRepository branchRepository,
    IWorkflowInstanceRepository instanceRepository,
    IParallelBranchService branchService,
    IWorkflowTaskRepository taskRepository,
    IBranchJoinCompletionService joinCompletion)
    : IRequestHandler<ResolveBranchChildCommand, Result>
{
    public async Task<Result> Handle(
        ResolveBranchChildCommand request,
        CancellationToken cancellationToken)
    {
        // F1: Load instance by PublicId (T2 AK) and verify tenant ownership
        var instanceResult = await instanceRepository.GetWithHistoryAsync(
            request.InstanceId, cancellationToken);

        if (instanceResult.IsFailed)
            return Result.Fail(instanceResult.Errors);

        var instance = instanceResult.Value;

        if (instance.TenantId != request.TenantId)
            return Result.Fail(new ForbiddenError(
                "Cannot resolve branch child in another tenant's workflow instance."));

        // Load the active branch status for this instance (by PublicId)
        var branchStatus = await branchRepository.GetInstanceBranchStatusAsync(
            request.InstanceId, cancellationToken);

        if (branchStatus is null)
            return Result.Fail(new NotFoundError(
                $"No active branch status found for instance '{request.InstanceId}'."));

        // Verify the child status belongs to this branch status — ChildStatusId is long (T1)
        var childStatus = branchStatus.ChildStatuses
            .FirstOrDefault(cs => cs.Id == request.ChildStatusId);

        if (childStatus is null)
            return Result.Fail(new NotFoundError(
                $"BranchChildStatus '{request.ChildStatusId}' not found."));

        if (childStatus.ResolutionType.HasValue)
            return Result.Fail(new Error(
                $"BranchChildStatus '{request.ChildStatusId}' is already resolved."));

        // F2: Verify the requesting user is the assigned approver
        // GetByBranchChildStatusIdAsync takes long (T1 PK)
        var task = await taskRepository.GetByBranchChildStatusIdAsync(
            request.ChildStatusId, cancellationToken);

        if (task is null)
            return Result.Fail(new NotFoundError(
                "No active task found for this branch child — resolution requires an assigned task."));

        if (task.AssigneeId != request.ResolvedBy)
            return Result.Fail(new ForbiddenError(
                "Only the assigned approver can resolve this branch child."));

        // F4: Service returns Result — ConflictError on DbUpdateConcurrencyException
        // childStatus.BranchChildId is long (T1 WorkflowBranchChild.Id)
        var resolveResult = await branchService.ResolveBranchChildAsync(
            branchStatus,
            childStatus.BranchChildId,
            request.ResolutionType,
            request.ResolvedBy,
            request.Comment,
            cancellationToken);

        if (resolveResult.IsFailed)
            return Result.Fail(resolveResult.Errors);

        var (joinComplete, continuationStateId) = resolveResult.Value;

        if (!joinComplete || continuationStateId is null)
            return Result.Ok();

        // Join satisfied — delegate the continuation transition (pre-check fail-CLOSED + advance) to the
        // shared join-completion path. This is the same logic that previously lived inline here; it is
        // now reused by the auto-resolve task handlers (complete/cancel) so all three share one path.
        return await joinCompletion.CompleteJoinAsync(
            instance, continuationStateId.Value, request.ResolvedBy, cancellationToken);
    }
}
