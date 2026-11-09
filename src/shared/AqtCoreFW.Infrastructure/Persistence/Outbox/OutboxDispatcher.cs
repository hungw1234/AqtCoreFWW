using System.Text.Json;
using AqtCoreFW.Infrastructure.Configuration;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// Polling outbox dispatcher (Phase 02c, v2.133).
/// One scoped instance per polling cycle. Reads pending rows from each registered
/// schema, deserialises the payload back to its concrete type, publishes through
/// MassTransit, and updates row state (processed / retry / dead-letter).
///
/// SAFETY:
///   - Schema names are taken from <see cref="IOutboxSchemaRegistry"/>, populated only
///     via the typed extension. Registry itself rejects unsafe identifiers.
///   - <see cref="IOutboxStore"/> uses Dapper parameters for all values; schema names
///     are interpolated only after registry validation.
///   - Logs include only Id / EventType / RetryCount, never Payload (PII guard).
///   - Event type resolution goes through <see cref="IOutboxEventTypeResolver"/> (stable
///     FullName token, with legacy AssemblyQualifiedName fallback) rather than a raw
///     <see cref="Type.GetType(string, bool)"/> call — an assembly version bump no longer
///     dead-letters every pending row.
/// </summary>
public sealed class OutboxDispatcher(
    IOutboxStore store,
    IBus bus,
    IOutboxSchemaRegistry registry,
    IOutboxEventTypeResolver eventTypeResolver,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    private static readonly JsonSerializerOptions DeserializeOptions = new();

    public async Task<OutboxDispatchSummary> DispatchBatchAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (!opts.EnableDispatcher)
        {
            logger.LogDebug("Outbox dispatcher disabled by configuration. Skipping cycle.");
            return OutboxDispatchSummary.Empty;
        }

        if (registry.Schemas.Count == 0)
        {
            logger.LogDebug("No schemas registered with the outbox dispatcher. Skipping cycle.");
            return OutboxDispatchSummary.Empty;
        }

        int dispatched = 0, retried = 0, deadLettered = 0;

        foreach (var schema in registry.Schemas)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<OutboxRow> batch;
            try
            {
                batch = await store.ClaimBatchAsync(schema, opts.BatchSize, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Outbox dispatcher failed to claim batch from schema {Schema}.", schema);
                continue; // try other schemas; transient failures resolve next cycle
            }

            if (batch.Count == 0) continue;

            logger.LogDebug(
                "Outbox dispatch starting for schema {Schema}. BatchSize={Count}.",
                schema, batch.Count);

            foreach (var row in batch)
            {
                var outcome = await DispatchRowAsync(schema, row, opts, ct);
                switch (outcome)
                {
                    case OutboxDispatchOutcome.Dispatched: dispatched++; break;
                    case OutboxDispatchOutcome.Retry: retried++; break;
                    case OutboxDispatchOutcome.DeadLettered: deadLettered++; break;
                }
            }
        }

        if (dispatched + retried + deadLettered > 0)
            logger.LogInformation(
                "Outbox dispatch cycle complete. Dispatched={Dispatched} Retried={Retried} DeadLettered={DeadLettered}.",
                dispatched, retried, deadLettered);

        return new OutboxDispatchSummary(dispatched, retried, deadLettered);
    }

    private async Task<OutboxDispatchOutcome> DispatchRowAsync(
        string schema, OutboxRow row, OutboxOptions opts, CancellationToken ct)
    {
        // 1. Resolve type via the stable-token resolver (FullName map + legacy AQN
        //    fallback). Null = truly unresolvable (type renamed/removed, or a corrupted
        //    token) → fatal poison, dead-letter immediately.
        var eventType = eventTypeResolver.Resolve(row.EventType);
        if (eventType is null)
        {
            logger.LogError(
                "Outbox row {Id} has unresolvable EventType {EventType}. Dead-lettering.",
                row.Id, row.EventType);
            await store.MoveToDeadLetterAsync(
                schema, row,
                $"Outbox event type '{row.EventType}' is unresolvable (no FullName match, no legacy AssemblyQualifiedName match).",
                ct);
            return OutboxDispatchOutcome.DeadLettered;
        }

        // 2. Deserialise + publish + mark processed
        try
        {
            var evt = JsonSerializer.Deserialize(row.Payload, eventType, DeserializeOptions);
            if (evt is null)
                throw new InvalidOperationException(
                    "Outbox payload deserialised to null — payload is probably the JSON literal 'null'.");

            await bus.Publish(evt, eventType, ct);
            await store.MarkProcessedAsync(schema, row.Id, ct);

            logger.LogDebug(
                "Outbox row {Id} ({EventType}) dispatched.", row.Id, eventType.Name);
            return OutboxDispatchOutcome.Dispatched;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return await HandleRowFailureAsync(schema, row, ex, opts, ct);
        }
    }

    private async Task<OutboxDispatchOutcome> HandleRowFailureAsync(
        string schema, OutboxRow row, Exception ex, OutboxOptions opts, CancellationToken ct)
    {
        var newRetryCount = row.RetryCount + 1;

        if (newRetryCount >= opts.DeadLetterAfterRetries)
        {
            logger.LogError(ex,
                "Outbox row {Id} ({EventType}) exhausted retries ({Retries}). Dead-lettering.",
                row.Id, row.EventType, newRetryCount);
            await store.MoveToDeadLetterAsync(schema, row, ex.Message, ct);
            return OutboxDispatchOutcome.DeadLettered;
        }

        logger.LogWarning(ex,
            "Outbox row {Id} ({EventType}) failed (attempt {Retry}). Will retry.",
            row.Id, row.EventType, newRetryCount);
        await store.MarkRetryAsync(schema, row.Id, newRetryCount, ex.Message, ct);
        return OutboxDispatchOutcome.Retry;
    }
}
