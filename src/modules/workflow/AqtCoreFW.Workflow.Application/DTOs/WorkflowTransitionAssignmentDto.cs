using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection of WorkflowTransitionAssignment — Save-graph response + read-back for FE designer.
/// Plan 260523-1241 Phase 02 (Option A).
/// </summary>
public sealed record WorkflowTransitionAssignmentDto(
    AssignmentCombineMode CombineMode,
    bool AllowAssignorOverride,
    IReadOnlyList<WorkflowTransitionAssignmentClauseDto> Clauses);

/// <summary>
/// Read projection of one resolution clause. RefsJson MAY be canonical (R3 Trim+ToUpperInvariant)
/// when persisted post-validator.
/// </summary>
public sealed record WorkflowTransitionAssignmentClauseDto(
    AssignmentClauseMode Mode,
    string? RefsJson);
