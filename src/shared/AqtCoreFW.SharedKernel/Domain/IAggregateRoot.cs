using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>Marker interface for DDD aggregate roots with domain event collection.</summary>
public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void AddDomainEvent(IDomainEvent domainEvent);
    void ClearDomainEvents();
}
