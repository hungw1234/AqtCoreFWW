using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Workflow.Domain.Events;

/// <summary>
/// Internal domain event raised by SlaBreachCheckerJob when a WorkflowInstance
/// has exceeded the configured SLA duration for its current state.
/// Consumed by notification handler to alert configured roles.
/// InstancePublicId: Guid — WorkflowInstance.PublicId (T2 AK).
/// CurrentStateInternalId: long — WorkflowState.Id (T1, no AK).
/// DefinitionPublicId: Guid — WorkflowDefinition.PublicId (T2 AK).
/// TenantId: Guid (FK exception).
/// </summary>
public sealed record SlaBreachedEvent(
    Guid InstancePublicId,
    long CurrentStateInternalId,
    Guid DefinitionPublicId,
    Guid TenantId,
    DateTimeOffset StateEnteredAt,
    int ElapsedHours,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
