using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection of WorkflowDefinition with embedded States, Transitions and parallel Branches.
/// Returned by GetWorkflowDefinitionQuery and CreateWorkflowDefinition command.
/// Phase-4b B4c: T2 entity — Id is Guid sourced from PublicId.
/// </summary>
public sealed record WorkflowDefinitionDto(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    string DefinitionKey,
    int Version,
    bool IsLatest,
    string EntityType,
    IReadOnlyList<WorkflowStateDto> States,
    IReadOnlyList<WorkflowTransitionDto> Transitions,
    IReadOnlyList<WorkflowBranchDto> Branches);

/// <summary>
/// GET read-back shape for one parallel branch. State references are long IDs (snapshot-internal-
/// consistent — the FE canvas keys nodes by state id). PositionX/Y restore the dragged fork diamond
/// on reload; null ⇒ the canvas auto-derives an initial position.
/// </summary>
public sealed record WorkflowBranchDto(
    long Id,
    long BranchingStateId,
    long ContinuationStateId,
    ParallelBranchType BranchType,
    int? RequiredCount,
    bool AllowCancelOnAnyReject,
    int TimeoutMinutes,
    double? PositionX,
    double? PositionY,
    IReadOnlyList<WorkflowBranchChildDto> Children);

public sealed record WorkflowBranchChildDto(
    long Id,
    long TargetStateId,
    string Name,
    string DisplayNameVi,
    string? DepartmentId,
    Guid? DefaultAssigneeId,
    int SortOrder,
    IReadOnlyList<WorkflowBranchResolutionDto> Resolutions,
    // Per-child handler-assignment rule (pool resolution). Reuses the transition assignment DTO shape
    // (structurally identical; FE branch editor reuses the transition assignment editor). Null = no rule.
    WorkflowTransitionAssignmentDto? Assignment = null);

public sealed record WorkflowBranchResolutionDto(
    BranchResolutionType ResolutionType,
    long? ResultingStateId);
