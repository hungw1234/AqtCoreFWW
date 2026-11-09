namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection of WorkflowState — safe to expose over API.
/// Phase-4b B4c: T1 entity — Id is long (bigint identity PK, DB-assigned).
///   AutoTransitionOnTimeoutId: long? (FK to T1 WorkflowTransition.Id).
/// </summary>
public sealed record WorkflowStateDto(
    // arch:accept-long T1 WorkflowState: aggregate-internal id, FE round-trips opaque
    long Id,
    string Name,
    string DisplayNameVi,
    string DisplayNameEn,
    bool IsInitial,
    bool IsFinal,
    string Color,
    int SortOrder,
    int? SlaHours,
    // arch:accept-long T1 WorkflowTransition: FK to timeout transition, FE round-trips opaque
    long? AutoTransitionOnTimeoutId,
    double? PositionX = null,
    double? PositionY = null);
