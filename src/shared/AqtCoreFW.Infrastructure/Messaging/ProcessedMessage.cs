namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// Idempotency-ledger row: one per (consumer, message dedup-key) that a consumer has claimed.
/// Table <c>messaging.processed_messages</c>. Written by <see cref="ProcessedMessageStore"/> — the
/// UNIQUE index on (ConsumerName, DedupKey) is what makes a duplicate claim a no-op. Pruned by
/// <see cref="ProcessedMessageCleanupJob"/> so the ledger stays bounded (one row per delivered message).
/// No tenant column — this is a cross-cutting delivery concern, not tenant-scoped data.
/// </summary>
public sealed class ProcessedMessage
{
    public long Id { get; set; }

    /// <summary>Stable consumer identity (e.g. <c>nameof(FormSubmissionAcknowledgedConsumer)</c>).</summary>
    public string ConsumerName { get; set; } = string.Empty;

    /// <summary>Stable per-message key from the event payload (SubmissionId / AuditEntryId / composite).</summary>
    public string DedupKey { get; set; } = string.Empty;

    /// <summary>When the claim was recorded — the retention cutoff column for the cleanup sweep.</summary>
    public DateTimeOffset ProcessedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
