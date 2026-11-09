namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>Per-message outcome from a single dispatcher pass.</summary>
public enum OutboxDispatchOutcome
{
    /// <summary>Successfully published to the bus and marked processed.</summary>
    Dispatched,
    /// <summary>Transient failure — RetryCount incremented, row remains unprocessed.</summary>
    Retry,
    /// <summary>Retry budget exhausted (or fatal poison) — moved to dead-letter table.</summary>
    DeadLettered
}

/// <summary>Aggregate counts produced by a dispatcher batch run.</summary>
public sealed record OutboxDispatchSummary(
    int Dispatched,
    int Retried,
    int DeadLettered)
{
    public int Total => Dispatched + Retried + DeadLettered;
    public static readonly OutboxDispatchSummary Empty = new(0, 0, 0);
}
