namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// Narrow data-access surface for the outbox dispatcher. Decouples the dispatch
/// loop (pure logic) from raw SQL — enables unit tests with substitutes.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> unprocessed rows from
    /// <c>[schema].OutboxMessages</c> using a single-statement CTE
    /// (UPDLOCK/READPAST/ROWLOCK + UPDATE...OUTPUT). Sets <c>ClaimedAtUtc</c> on
    /// each returned row so concurrent replicas observe disjoint sets.
    /// Stale claims (older than 5 minutes) are reclaimable to cover crash-after-claim.
    /// Caller MUST follow up with MarkProcessed / MarkRetry / MoveToDeadLetter —
    /// each releases the claim.
    /// </summary>
    Task<IReadOnlyList<OutboxRow>> ClaimBatchAsync(
        string schema, int batchSize, CancellationToken ct);

    /// <summary>Sets <c>ProcessedAtUtc=SYSUTCDATETIME()</c> + clears Error.</summary>
    Task MarkProcessedAsync(string schema, Guid id, CancellationToken ct);

    /// <summary>Increments RetryCount + records Error. Row remains unprocessed.</summary>
    Task MarkRetryAsync(
        string schema, Guid id, int newRetryCount, string error, CancellationToken ct);

    /// <summary>
    /// Inserts a row into <c>messaging.dead_letters</c> and marks the source row
    /// processed in a single transaction. Idempotent at the application level —
    /// caller MUST gate on retry threshold first.
    /// </summary>
    Task MoveToDeadLetterAsync(
        string schema, OutboxRow row, string reason, CancellationToken ct);
}

/// <summary>Lite projection used by the dispatcher (Payload included for DLQ insert).</summary>
public sealed record OutboxRow(
    Guid Id,
    string EventType,
    string Payload,
    int RetryCount);
