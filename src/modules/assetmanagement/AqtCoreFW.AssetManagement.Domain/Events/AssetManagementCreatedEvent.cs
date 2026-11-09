using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.AssetManagement.Domain.Events;

public sealed record AssetManagementCreatedEvent(
    Guid EntityId,
    Guid TenantId,
    Guid CreatedBy) : IInternalDomainEvent
{
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
