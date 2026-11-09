using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateParallelBranch;

/// <summary>
/// Creates a WorkflowParallelBranch definition (with children + resolutions)
/// for a given workflow definition state.
/// Phase-4b B4c: DefinitionId = long (internal bigint PK of T2 WorkflowDefinition).
///               BranchingStateId, ContinuationStateId = long (T1 WorkflowState.Id — no Guid AK).
/// </summary>
[Obsolete("Superseded by SaveDefinitionGraph branches[]; not dispatched. " +
    "Parallel branches are authored/persisted name-keyed via the replace-all graph save (ReplaceGraphAsync), " +
    "not this long-keyed insert-only command.")]
public sealed record CreateParallelBranchCommand(
    long DefinitionId,
    long BranchingStateId,
    long ContinuationStateId,
    ParallelBranchType BranchType,
    int? RequiredCount,
    bool AllowCancelOnAnyReject,
    int TimeoutMinutes,
    IReadOnlyList<CreateBranchChildRequest> Children)
    : IRequest<Result<long>>;

/// <summary>
/// Represents a single child branch in the create request.
/// TargetStateId = long (T1 WorkflowState.Id — no Guid AK).
/// </summary>
public sealed record CreateBranchChildRequest(
    long TargetStateId,
    string Name,
    string DisplayNameVi,
    string? DepartmentId,
    int SortOrder,
    IReadOnlyList<CreateBranchResolutionRequest> Resolutions,
    Guid? DefaultAssigneeId = null);

/// <summary>
/// Maps a resolution type to an optional redirect state.
/// ResultingStateId = long? (T1 WorkflowState.Id — no Guid AK).
/// </summary>
public sealed record CreateBranchResolutionRequest(
    BranchResolutionType ResolutionType,
    long? ResultingStateId);
