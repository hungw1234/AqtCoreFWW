namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// Persisted record of a message that exhausted all retries and landed in DLQ.
/// Table: messaging.dead_letters — written by DeadLetterConsumer.
/// Status: Pending → Quarantined (admin action) or Retried (admin requeue).
/// </summary>
public sealed class MessageDeadLetter
{
    public Guid Id { get; set; }

    /// <summary>Full type name of the original message contract.</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>JSON-serialized message body. IDs only — no PII per architecture rules.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Last exception message / fault reason from MassTransit.</summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>Source queue name that produced this dead letter.</summary>
    public string OriginalQueue { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Pending;

    /// <summary>Set when admin triggers a retry.</summary>
    public DateTimeOffset? RetriedAt { get; set; }

    /// <summary>Admin note explaining quarantine reason (optional).</summary>
    public string? QuarantineReason { get; set; }
}

public enum DeadLetterStatus
{
    Pending = 0,
    Quarantined = 1,
    Retried = 2
}
