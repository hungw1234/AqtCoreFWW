using System.Text.Json;
using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// EF SaveChanges interceptor — collects IExternalDomainEvent from aggregates and persists
/// them as OutboxMessages in the same transaction.
/// IInternalDomainEvents are dispatched in-process by IDomainEventPublisher (Phase 02).
/// External events use Outbox for at-least-once delivery via MassTransit (Phase 02c).
/// Singleton — IGuidGenerator is also singleton, ctor injection is safe.
/// </summary>
public sealed class OutboxInterceptor(IGuidGenerator guidGen) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            await CollectOutboxMessagesAsync(eventData.Context, cancellationToken);

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            CollectOutboxMessages(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private async Task CollectOutboxMessagesAsync(
        Microsoft.EntityFrameworkCore.DbContext context,
        CancellationToken cancellationToken)
    {
        var messages = BuildOutboxMessages(context);
        if (messages.Count > 0)
            await context.Set<OutboxMessage>().AddRangeAsync(messages, cancellationToken);
    }

    private void CollectOutboxMessages(Microsoft.EntityFrameworkCore.DbContext context)
    {
        var messages = BuildOutboxMessages(context);
        if (messages.Count > 0)
            context.Set<OutboxMessage>().AddRange(messages);
    }

    private List<OutboxMessage> BuildOutboxMessages(Microsoft.EntityFrameworkCore.DbContext context)
    {
        var aggregates = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var messages = aggregates
            .SelectMany(a => a.DomainEvents.OfType<IExternalDomainEvent>())
            .Select(evt => new OutboxMessage
            {
                Id = guidGen.NewIdMonotonic(),
                // Stable token — namespace + type name only, no assembly version/culture/PKT.
                // Survives an assembly version bump between write and dispatch time; resolved
                // back to a Type by IOutboxEventTypeResolver (AQN fallback covers legacy rows).
                EventType = evt.GetType().FullName!,
                Payload = JsonSerializer.Serialize(evt, evt.GetType(), SerializerOptions),
                CreatedAtUtc = DateTimeOffset.UtcNow
            })
            .ToList();

        // Only clear external events (collected into outbox). Preserve internal events
        // for in-process MediatR dispatch by IDomainEventPublisher after SaveChanges.
        foreach (var aggregate in aggregates)
        {
            var internalEvents = aggregate.DomainEvents
                .Where(e => e is not IExternalDomainEvent).ToList();
            aggregate.ClearDomainEvents();
            foreach (var ie in internalEvents)
                aggregate.AddDomainEvent(ie);
        }

        return messages;
    }
}
