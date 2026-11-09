namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read model for WorkflowAssignmentRule — returned by CRUD and resolve queries.
/// Phase-4b B4c: T1 entity — Id is long (bigint identity PK).
/// Phase-07 PR-D2: WorkflowDefinitionId is Guid (T2 WorkflowDefinition.PublicId AK).
/// </summary>
public sealed record WorkflowAssignmentRuleDto(
    long Id,
    Guid TenantId,
    Guid WorkflowDefinitionId,
    string? EntityType,
    string? EntitySubType,
    int Specificity,
    int Priority,
    bool IsActive,
    string? Description,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil);
