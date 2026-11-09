namespace AqtCoreFW.Workflow.Application.Queries.GetMyProcessedInstances;

/// <summary>
/// Single item in the "dossiers I have processed" list.
/// EntityType + EntityId identify the linked business object — FE resolves display name from
/// its own module data (no Workflow→BusinessProcess cross-module dependency).
/// currentStateName is resolved from WorkflowStates (in-module JOIN — no cross-module query).
/// isFinal reflects the WorkflowState.IsFinal flag; a final instance also has CompletedAt set.
/// </summary>
public sealed record MyProcessedInstanceDto(
    Guid WorkflowInstancePublicId,
    long CurrentStateId,
    string CurrentStateName,
    string EntityType,
    Guid EntityId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool IsFinal);
