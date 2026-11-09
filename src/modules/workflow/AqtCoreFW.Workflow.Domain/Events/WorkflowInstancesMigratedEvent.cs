using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Workflow.Domain.Events;

/// <summary>
/// Published after WorkflowMigrationExecutor completes a batch migration.
/// Consumed by WorkflowInstancesMigratedEventHandler to invalidate analytics cache
/// for both source and target definition IDs (H05).
/// IInternalDomainEvent — dispatched via MediatR in same process.
/// SourceDefinitionPublicId: Guid — WorkflowDefinition.PublicId (T2 AK).
/// TargetDefinitionPublicId: Guid — WorkflowDefinition.PublicId (T2 AK).
/// TenantId: Guid (FK exception).
/// </summary>
public sealed record WorkflowInstancesMigratedEvent(
    Guid SourceDefinitionPublicId,
    Guid TargetDefinitionPublicId,
    Guid TenantId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
