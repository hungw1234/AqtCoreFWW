namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection of WorkflowTransition — safe to expose over API.
/// Phase-4b B4c: T1 entity — Id, FromStateId, ToStateId are long (bigint identity PKs).
/// </summary>
public sealed record WorkflowTransitionDto(
    // arch:accept-long T1 WorkflowTransition: aggregate-internal id, FE round-trips opaque
    long Id,
    // arch:accept-long T1 WorkflowTransition: graph FK addressed by Name natural key in SaveDefinitionGraph
    long FromStateId,
    // arch:accept-long T1 WorkflowTransition: graph FK addressed by Name natural key in SaveDefinitionGraph
    long ToStateId,
    string ActionName,
    string ActionLabelVi,
    string ActionLabelEn,
    string? ConditionsJson,
    int SortOrder,
    bool IsRecall,
    string? SourceHandle = null,
    string? TargetHandle = null)
{
    /// <summary>
    /// Optional assignment projection (Phase 5c-followup plan 260523-1241).
    /// Null when transition has no <see cref="AqtCoreFW.Workflow.Domain.Entities.WorkflowTransitionAssignment"/> row.
    /// </summary>
    public WorkflowTransitionAssignmentDto? Assignment { get; init; }
}
