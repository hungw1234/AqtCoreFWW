namespace AqtCoreFW.SharedKernel.Application;

/// <summary>
/// Consumer-side idempotency ledger for the outbox at-least-once delivery path.
///
/// A message may be delivered more than once — the custom <c>OutboxDispatcher</c> can republish a row
/// if it crashes between <c>bus.Publish</c> and <c>MarkProcessed</c> (it does not pin a stable transport
/// MessageId, so MassTransit's own inbox cannot dedup that), and a broker can redeliver. A consumer whose
/// effect is NOT naturally idempotent must therefore claim a dedup key BEFORE performing that effect and
/// skip when the claim is refused.
/// </summary>
public interface IProcessedMessageStore
{
    /// <summary>
    /// Atomically records that <paramref name="consumerName"/> has handled the message identified by
    /// <paramref name="dedupKey"/>. Returns <c>true</c> the FIRST time the pair is seen (caller SHOULD
    /// proceed with its effect) and <c>false</c> if it was already recorded (a duplicate — caller MUST
    /// skip the effect). Atomicity is enforced by a UNIQUE (consumer, key) index, not a read-then-write,
    /// so it is race-safe. The key is per-consumer: different consumers handling the same message each
    /// get their own first-time claim.
    /// </summary>
    /// <param name="consumerName">Stable consumer identity, e.g. <c>nameof(MyConsumer)</c>.</param>
    /// <param name="dedupKey">Stable per-message key carried in the event payload (survives redispatch).</param>
    Task<bool> TryClaimAsync(string consumerName, string dedupKey, CancellationToken ct = default);
}
