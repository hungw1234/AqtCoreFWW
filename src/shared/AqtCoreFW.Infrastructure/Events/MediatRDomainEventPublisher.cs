using AqtCoreFW.SharedKernel.Domain.Events;
using MediatR;

namespace AqtCoreFW.Infrastructure.Events;

/// <summary>
/// Dispatches internal domain events in-process via MediatR IPublisher.
/// External domain events are handled by MassTransit EF Outbox (Phase 02c).
/// </summary>
public sealed class MediatRDomainEventPublisher(IPublisher publisher) : IDomainEventPublisher
{
    public async Task PublishAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in events)
        {
            await publisher.Publish(domainEvent, cancellationToken);
        }
    }
}
