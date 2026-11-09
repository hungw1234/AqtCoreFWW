# ADR-005: Transactional Outbox for Domain Events (MassTransit + EF)

**Date:** 2026-03-04
**Status:** Accepted
**Deciders:** AqtCoreFW Architecture Team

## Context

Reliable domain event delivery is critical in modular monolith architecture. Without careful coordination, events can be lost or published twice:
- **Dual-write problem:** Application writes aggregate to database AND publishes event to message broker in separate operations; if crash occurs between writes, event is lost or aggregate is orphaned
- **Exactly-once delivery:** Impossible; message brokers guarantee at-least-once; consumers must be idempotent
- **Transactional boundaries:** Publishing to RabbitMQ outside database transaction loses atomicity

MassTransit's EF Core Outbox pattern solves this: domain events written to `OutboxMessage` table in same database transaction, background job asynchronously delivers messages to RabbitMQ. If the background job crashes, messages remain in Outbox and are retried.

## Decision

Implement **MassTransit EF Core Outbox** for reliable domain event publishing:
- Domain events published via `IPublishEndpoint.Publish(event)` within aggregate save transaction
- MassTransit writes event to `[Audit].[OutboxMessage]` table (same transaction as aggregate)
- Background job (`OutboxDeliveryService` via Hangfire) polls Outbox every 5 seconds
- Successful delivery: message marked as delivered in OutboxMessage; record kept for 24h audit
- Consumer idempotency: `[Audit].[InboxState]` table tracks processed message IDs; duplicate messages silently dropped
- At-least-once delivery guaranteed; consumer code must be idempotent

This approach combines EF transaction safety with reliable async messaging without distributed transactions or consensus overhead.

## Consequences

### Positive
- **Transaction Safety:** Domain events committed in same database transaction as aggregate; no lost messages on crash
- **Reliable Delivery:** Outbox polling ensures eventual delivery even if RabbitMQ temporarily unavailable
- **Consumer Idempotency:** InboxState tracks message IDs; duplicates safely ignored (automatic via MassTransit)
- **At-Least-Once Guarantee:** No message loss; duplicates handled by idempotent consumers
- **Auditability:** Outbox/Inbox tables retained for 24h; audit trail of event publishing visible
- **Simplicity:** No distributed transactions (two-phase commit), no compensating transactions, no saga pattern complexity

### Negative
- **Slight Latency:** Events not published immediately; 5-second polling delay before message enters RabbitMQ (acceptable for GOV workflows)
- **Table Growth:** OutboxMessage and InboxState accumulate; 24-hour retention requires archive/cleanup job
- **Eventual Consistency:** Subscribers receive events 5+ seconds after aggregate changes; read models eventually consistent
- **Consumer Complexity:** Consumer must implement idempotency handler; multiple invocations possible if RabbitMQ retries
- **Troubleshooting:** Debugging stale Outbox messages requires manual intervention in OutboxMessage table

## Alternatives Considered

| Option | Why Rejected |
|--------|-------------|
| **Direct RabbitMQ Publishing** | Dual-write problem; lost messages on crash between DB and broker writes |
| **Saga Pattern (Choreography)** | Event chain complexity; difficult to debug; requires additional coordination logic |
| **Distributed Transactions (DTC)** | GOV infrastructure often disables MSDTC; complex failure handling; performance impact |
| **Change Data Capture (CDC)** | SQL Server CDC increases complexity; requires CDC infrastructure; not available on all SQL Server editions |
| **Event Sourcing** | Architectural shift; requires event store instead of aggregate snapshots; higher learning curve |

## Implementation Details

### Table Structure
```sql
-- Outbox: domain events awaiting delivery
CREATE TABLE [Audit].[OutboxMessage] (
    [Id] BIGINT PRIMARY KEY IDENTITY(1,1),
    [ExceptionId] UNIQUEIDENTIFIER,
    [MessageId] UNIQUEIDENTIFIER NOT NULL UNIQUE,
    [ContentType] NVARCHAR(256) NOT NULL,
    [Body] VARBINARY(MAX) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [DeliveredAt] DATETIME2,
    [DispatchedAt] DATETIME2,
    [ExpiresAt] DATETIME2
);

-- Inbox: processed message IDs for deduplication
CREATE TABLE [Audit].[InboxState] (
    [Id] BIGINT PRIMARY KEY IDENTITY(1,1),
    [MessageId] UNIQUEIDENTIFIER NOT NULL UNIQUE,
    [ConsumerType] NVARCHAR(256) NOT NULL,
    [Received] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ReceiveCount] INT NOT NULL DEFAULT 1,
    [ExpiresAt] DATETIME2
);

-- Cleanup job runs daily; deletes InboxState/OutboxMessage older than 24h
CREATE PROCEDURE [Audit].[DeleteExpiredOutboxMessages]
AS BEGIN
    DELETE FROM [Audit].[OutboxMessage]
    WHERE DeliveredAt IS NOT NULL AND DeliveredAt < DATEADD(DAY, -1, GETUTCDATE());

    DELETE FROM [Audit].[InboxState]
    WHERE ExpiresAt < GETUTCDATE();
END;
```

### Publishing Domain Events
```csharp
// In aggregate save handler (e.g., CasesCommandHandler)
public async Task Handle(CreateCaseCommand cmd, IUnitOfWork uow)
{
    var caseAggregate = Case.Create(cmd.Title, cmd.Description);

    // Save aggregate
    await uow.Cases.AddAsync(caseAggregate);
    await uow.SaveChangesAsync(); // Commits aggregate + domain events in same transaction

    // Domain events automatically published to Outbox by MassTransit interceptor
    // No manual _publishEndpoint.Publish() needed; SaveChanges triggers event publishing
}
```

### Consumer Idempotency
```csharp
public class CaseCreatedEventConsumer : IConsumer<CaseCreatedEvent>
{
    public async Task Consume(ConsumeContext<CaseCreatedEvent> context)
    {
        // MassTransit InboxState prevents duplicate processing
        // If message redelivered, handler still executes but idempotency key prevents double-booking
        await _notificationService.SendAsync(
            new EmailNotification
            {
                To = context.Message.CreatorEmail,
                CorrelationId = context.Message.CaseId.ToString() // Idempotency key
            }
        );
    }
}
```

### Background Job (Hangfire)
```csharp
// Configured in DI: Hangfire recurring job
RecurringJob.AddOrUpdate<OutboxDeliveryService>(
    "deliver-outbox-messages",
    x => x.DeliverAsync(),
    Cron.Every(5).Seconds
);

public class OutboxDeliveryService
{
    public async Task DeliverAsync()
    {
        var pending = await _auditDb.OutboxMessages
            .Where(m => m.DeliveredAt == null && m.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        foreach (var msg in pending)
        {
            try
            {
                await _endpoint.Publish(msg.Body);
                msg.DeliveredAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                // Retry on next job execution; exponential backoff via MassTransit
                _logger.LogWarning($"Outbox delivery failed: {ex.Message}");
            }
        }

        await _auditDb.SaveChangesAsync();
    }
}
```

## Mitigation Strategy

- **Monitoring:** Alert if Outbox has pending messages > 1 minute old (indicates delivery service failure)
- **Idempotency Testing:** Unit tests verify consumer processes same message twice without side effects
- **Archive Strategy:** Move delivered OutboxMessages older than 24h to cold storage; keep InboxState for 7 days
- **Phase 2 Enhancement:** Add HSM-signed OutboxMessage for audit trail integrity; track event publishing in HMAC audit log
