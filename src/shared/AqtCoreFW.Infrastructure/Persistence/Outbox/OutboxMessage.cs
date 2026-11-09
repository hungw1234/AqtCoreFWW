namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// Outbox message — persisted in the same DB transaction as the aggregate change.
/// Processed asynchronously by OutboxProcessor or MassTransit EF Outbox (Phase 02c).
/// IMPORTANT: Payload MUST NOT contain PII for IExternalDomainEvent — IDs only (ArchUnit Phase 10).
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable type token (<c>Type.FullName</c>) of the domain event, resolved back to a Type via
    /// <c>IOutboxEventTypeResolver</c> at dispatch. Legacy rows written with an assembly-qualified
    /// name still resolve via the resolver's fallback (version-agnostic — survives assembly bumps).
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>JSON-serialized event payload. External events: IDs only, no PII.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when successfully dispatched. Null = pending processing.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>Last error message if processing failed.</summary>
    public string? Error { get; set; }

    public int RetryCount { get; set; }

    /// <summary>
    /// Claim lock for atomic dispatch (OUTBOX-1-DISP, v2.133p12).
    /// Set by ClaimBatchAsync via single-statement CTE. NULL = unclaimed.
    /// Stale claims (older than OutboxOptions.ClaimTimeout) are reclaimable.
    /// MarkRetry/MarkProcessed/MoveToDeadLetter clear this back to NULL.
    /// </summary>
    public DateTimeOffset? ClaimedAtUtc { get; set; }

    /// <summary>Module schema that owns this message (e.g. "notifications", "identity").</summary>
    public string SchemaName { get; set; } = string.Empty;
}
