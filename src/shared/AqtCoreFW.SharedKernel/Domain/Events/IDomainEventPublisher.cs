namespace AqtCoreFW.SharedKernel.Domain.Events;

/// <summary>Dispatches collected domain events after SaveChanges completes.</summary>
public interface IDomainEventPublisher
{
    Task PublishAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);
}
