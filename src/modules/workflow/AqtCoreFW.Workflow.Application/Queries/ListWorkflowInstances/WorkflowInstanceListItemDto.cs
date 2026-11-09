namespace AqtCoreFW.Workflow.Application.Queries.ListWorkflowInstances;

/// <summary>
/// Projection DTO for a single row in the cursor-paginated instance list.
/// Phase-4b B4c: T2 WorkflowInstance — Id sourced from PublicId (Guid AK).
///   DefinitionId: long (FK to T2 WorkflowDefinition.Id internal bigint PK).
///   CurrentStateId: long (FK to T1 WorkflowState.Id bigint PK — no Guid AK).
///   InternalId: long (WorkflowInstance.Id bigint PK — used for cursor keyset only, not exposed to FE).
/// </summary>
public sealed record WorkflowInstanceListItemDto(
    Guid Id,                    // PublicId — T2 Guid AK (external identifier)
    long InternalId,            // Id — bigint PK (cursor keyset key, internal only)
    long DefinitionId,
    string EntityType,
    Guid EntityId,
    long CurrentStateId,
    DateTimeOffset StartedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt);
