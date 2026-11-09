using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateParallelBranch;

/// <summary>
/// Creates a WorkflowParallelBranch with its children and resolution mappings.
/// Returns the new branch long Id on success.
/// Phase-4b B4c: DefinitionId is long (internal bigint PK of T2 WorkflowDefinition).
///               State comparisons are long-on-long (T1 WorkflowState.Id).
///               Uses GetWithStatesAndTransitionsByInternalIdAsync (internal Id overload).
/// </summary>
[Obsolete("Dormant alongside CreateParallelBranchCommand — superseded by SaveDefinitionGraph branches[].")]
public sealed class CreateParallelBranchCommandHandler(
    IWorkflowDefinitionRepository definitionRepository,
    IParallelBranchRepository branchRepository)
    : IRequestHandler<CreateParallelBranchCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        CreateParallelBranchCommand request,
        CancellationToken cancellationToken)
    {
        // Load definition by internal long Id (command carries internal PK)
        var defResult = await definitionRepository.GetWithStatesAndTransitionsByInternalIdAsync(
            request.DefinitionId, cancellationToken);
        if (defResult.IsFailed)
            return Result.Fail(defResult.Errors);

        var definition = defResult.Value;

        // Validate BranchingState and ContinuationState belong to this definition (long comparison)
        if (definition.States.All(s => s.Id != request.BranchingStateId))
            return Result.Fail(new Error(
                $"BranchingStateId '{request.BranchingStateId}' not found in definition '{request.DefinitionId}'."));

        if (definition.States.All(s => s.Id != request.ContinuationStateId))
            return Result.Fail(new Error(
                $"ContinuationStateId '{request.ContinuationStateId}' not found in definition '{request.DefinitionId}'."));

        // NOfM requires RequiredCount
        if (request.BranchType == Domain.Enums.ParallelBranchType.NOfM
            && (request.RequiredCount is null or <= 0))
            return Result.Fail(new Error("RequiredCount must be > 0 for NOfM branch type."));

        // WorkflowParallelBranch.Create takes all long IDs (T1 entities)
        var branch = WorkflowParallelBranch.Create(
            request.DefinitionId,
            request.BranchingStateId,
            request.ContinuationStateId,
            request.BranchType,
            request.RequiredCount,
            request.AllowCancelOnAnyReject,
            request.TimeoutMinutes);

        // Build children with their resolution mappings (all long IDs)
        foreach (var childReq in request.Children)
        {
            // WorkflowBranchChild.Create: branchId long, targetStateId long
            var child = WorkflowBranchChild.Create(
                branch.Id,
                childReq.TargetStateId,
                childReq.Name,
                childReq.DisplayNameVi,
                childReq.DepartmentId,
                childReq.SortOrder,
                childReq.DefaultAssigneeId);

            foreach (var resReq in childReq.Resolutions)
            {
                // WorkflowBranchResolution.Create: childId long, resultingStateId long?
                var resolution = WorkflowBranchResolution.Create(
                    child.Id,
                    resReq.ResolutionType,
                    resReq.ResultingStateId);
                child.AddResolution(resolution);
            }

            branch.AddChild(child);
        }

        await branchRepository.AddAsync(branch, cancellationToken);

        // branch.Id is long (T1 PK, DB-assigned after AddAsync)
        return Result.Ok(branch.Id);
    }
}
