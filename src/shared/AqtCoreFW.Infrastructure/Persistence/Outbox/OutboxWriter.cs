using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain.Events;
using Dapper;

namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// Scoped <see cref="IOutboxWriter"/> — writes the OutboxMessage row via raw parameterized SQL
/// against the ambient <see cref="ICrossModuleUnitOfWork"/> connection + transaction (mirrors
/// <see cref="OutboxStore"/>'s SQL style), so it rides the SAME local SQL transaction as the
/// caller's business write instead of opening a second physical connection.
///
/// Serialization mirrors <see cref="OutboxInterceptor"/> EXACTLY — <c>EventType</c> is the bare
/// <c>Type.FullName</c> (no assembly-qualified name), <c>Payload</c> is
/// <see cref="JsonSerializer.Serialize(object?, Type, JsonSerializerOptions?)"/> against the
/// runtime type — so <see cref="IOutboxEventTypeResolver"/> and <see cref="OutboxDispatcher"/>
/// handle rows written here identically to interceptor-captured rows.
/// </summary>
public sealed class OutboxWriter(
    ICrossModuleUnitOfWork uow,
    IGuidGenerator guidGen,
    IOutboxSchemaRegistry schemaRegistry) : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public async Task EnqueueAsync(IExternalDomainEvent evt, string schemaName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);

        if (!uow.IsActive)
            throw new InvalidOperationException(
                $"{nameof(OutboxWriter)}.{nameof(EnqueueAsync)} requires an active {nameof(ICrossModuleUnitOfWork)}. " +
                "Call uow.BeginAsync() before enqueueing — an event enqueued outside the transaction " +
                "could commit even if the accompanying business write fails, defeating the point of " +
                "routing it through the outbox.");

        // Defence-in-depth: schema is interpolated into raw SQL below. Requiring it to already be
        // in the AddOutboxSchema(...) allowlist both blocks injection (registry validates the name
        // against a safe-character regex on Register) and catches a real config gap — an event
        // enqueued into an unregistered schema would sit forever, never polled by the dispatcher.
        var normalizedSchema = schemaName.Trim().ToLowerInvariant();
        if (!schemaRegistry.Schemas.Contains(normalizedSchema, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Outbox schema '{schemaName}' is not registered via AddOutboxSchema(...) in " +
                "InfrastructureRegistration — the dispatcher would never poll it.");

        var sql = $"""
            INSERT INTO [{normalizedSchema}].[OutboxMessages]
                (Id, EventType, Payload, CreatedAtUtc, ProcessedAtUtc, Error, RetryCount, ClaimedAtUtc, SchemaName)
            VALUES
                (@Id, @EventType, @Payload, @CreatedAtUtc, NULL, NULL, 0, NULL, @SchemaName)
            """;

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = guidGen.NewIdMonotonic(),
                // Stable token — see OutboxInterceptor for why this must be the bare FullName.
                EventType = evt.GetType().FullName!,
                Payload = JsonSerializer.Serialize(evt, evt.GetType(), SerializerOptions),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                SchemaName = normalizedSchema
            },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }
}
