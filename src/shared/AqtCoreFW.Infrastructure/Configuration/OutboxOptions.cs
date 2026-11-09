namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Outbox dispatcher configuration.
/// Phase 02c (v2.133) — production polling dispatcher uses these settings.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Polling interval in seconds between outbox processing batches.</summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>Maximum retries before a message is moved to the dead-letter table.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Maximum messages processed per polling cycle, per schema.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Number of dispatch failures before a row is moved to <c>messaging.dead_letters</c>
    /// and marked processed. Defaults to 3 — matches prior <see cref="MaxRetries"/> semantics.
    /// </summary>
    public int DeadLetterAfterRetries { get; set; } = 3;

    /// <summary>
    /// Kill switch — when false the hosted <c>OutboxProcessor</c> still runs but the
    /// dispatcher loop is skipped. Existing rows accumulate without loss; flip back to
    /// true to drain. Default true.
    /// </summary>
    public bool EnableDispatcher { get; set; } = true;

    /// <summary>
    /// Stale-claim recovery threshold (OUTBOX-1-DISP, v2.133p12).
    /// Rows with <c>ClaimedAtUtc &lt; (now - ClaimTimeout)</c> are reclaimable —
    /// covers dispatcher crash post-claim. Default 5 minutes.
    /// Tune SHORTER for faster recovery; LONGER if a dispatch may legitimately
    /// exceed 5 minutes (large batch, slow downstream bus).
    /// </summary>
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
