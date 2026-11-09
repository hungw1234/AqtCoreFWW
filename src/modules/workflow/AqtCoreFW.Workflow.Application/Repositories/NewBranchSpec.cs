using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Transport record passed from SaveDefinitionGraph handler to the graph validator and the
/// repository's <c>ReplaceGraphAsync</c>.
///
/// Why name-keyed (not pre-built entities): a graph save is replace-all — every WorkflowState row
/// is DELETEd and re-INSERTed, so each save regenerates the state IDENTITY. A branch's
/// BranchingStateId / ContinuationStateId / child TargetStateId / resolution ResultingStateId are
/// bare bigint columns with NO FK to WorkflowStates, so persisting them by the old long would leave
/// dangling references the moment the graph is re-saved. The stable identity of a state is its Name
/// (UX_WorkflowStates_DefinitionId_Name). The repository resolves these names → the freshly-assigned
/// long IDs via the same <c>stateIdByName</c> map transitions use, after states are persisted.
///
/// Lives in Application.Repositories (not the Commands namespace) because the Infrastructure repo
/// references this namespace, mirroring <see cref="NewTransitionSpec"/>.
/// </summary>
public sealed record NewBranchSpec(
    string BranchingStateName,
    string ContinuationStateName,
    ParallelBranchType BranchType,
    int? RequiredCount,
    bool AllowCancelOnAnyReject,
    int TimeoutMinutes,
    double? PositionX,
    double? PositionY,
    IReadOnlyList<NewBranchChildSpec> Children);

/// <summary>Name-keyed child sub-branch (TargetStateName resolved → long at persist).</summary>
public sealed record NewBranchChildSpec(
    string TargetStateName,
    string Name,
    string DisplayNameVi,
    string? DepartmentId,
    Guid? DefaultAssigneeId,
    int SortOrder,
    IReadOnlyList<NewBranchResolutionSpec> Resolutions)
{
    /// <summary>
    /// Optional per-child handler-assignment payload — reuses <see cref="AssignmentSpec"/> (transition
    /// transport). Null = no pool rule. Persisted in <c>ReplaceGraphAsync</c> via the child aggregate nav.
    /// </summary>
    public AssignmentSpec? Assignment { get; init; }
}

/// <summary>Name-keyed resolution outcome (ResultingStateName optional, resolved → long? at persist).</summary>
public sealed record NewBranchResolutionSpec(
    BranchResolutionType ResolutionType,
    string? ResultingStateName);
