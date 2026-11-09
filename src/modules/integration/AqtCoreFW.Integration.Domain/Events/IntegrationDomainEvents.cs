using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Integration.Domain.Events;

/// <summary>
/// Base for integration internal domain events.
/// MessageId is abstract — each concrete record provides it via positional ctor param (Rule1: no Guid.NewGuid() defaults).
/// </summary>
public abstract record IntegrationDomainEventBase : IInternalDomainEvent
{
    public abstract Guid MessageId { get; init; }
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Raised when a new integration partner is created.
/// PartnerPublicId: Guid (T2 AK — stable external identifier, set by factory pre-SaveChanges).
/// TenantId: Guid (FK exception list — tenant isolation key).
/// MessageId: caller-supplied via IGuidGenerator.NewIdRandom() (Rule1 compliant).
/// </summary>
public sealed record PartnerCreatedEvent(Guid PartnerPublicId, Guid TenantId, Guid MessageId) : IntegrationDomainEventBase;

/// <summary>
/// Raised when an integration partner is deactivated.
/// PartnerPublicId: Guid (T2 AK — stable external identifier, available from entity property).
/// TenantId: Guid (FK exception list — tenant isolation key).
/// MessageId: caller-supplied via IGuidGenerator.NewIdRandom() (Rule1 compliant).
/// </summary>
public sealed record PartnerDeactivatedEvent(Guid PartnerPublicId, Guid TenantId, Guid MessageId) : IntegrationDomainEventBase;
