namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection of WorkflowInstance.
/// History is populated by GetWorkflowInstanceQuery; empty list otherwise.
/// Phase-4b B4c: T2 entity — Id is Guid sourced from PublicId.
///   DefinitionId: long (FK to T2 WorkflowDefinition.Id — internal).
///   CurrentStateId: long (FK to T1 WorkflowState.Id — internal).
/// </summary>
public sealed record WorkflowInstanceDto(
    Guid Id,
    long DefinitionId,
    string EntityType,
    Guid EntityId,
    long CurrentStateId,
    Guid TenantId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset CurrentStateEnteredAt,
    IReadOnlyList<WorkflowInstanceHistoryDto> History);

/// <summary>
/// Single transition history record on a WorkflowInstance.
/// Enrichment fields (FromStateName/ToStateName/ActorName) populated by
/// GetWorkflowInstanceQueryHandler — null if underlying state/user was deleted.
/// Phase-4b B4c: T3 entity — Id is Guid sourced from MessageId (public AK).
///   FromStateId/ToStateId: long (FK to T1 WorkflowState.Id — internal).
/// </summary>
public sealed record WorkflowInstanceHistoryDto
{
    public Guid Id { get; init; }          // MessageId — T3 public AK
    public long FromStateId { get; init; }
    public long ToStateId { get; init; }
    public string ActionName { get; init; } = string.Empty;
    public Guid TransitionedBy { get; init; }
    public string? Comment { get; init; }
    public DateTimeOffset TransitionedAt { get; init; }
    public string? FromStateName { get; init; }
    public string? ToStateName { get; init; }
    public string? ActorName { get; init; }
    public string? ActionLabel { get; init; }
}
