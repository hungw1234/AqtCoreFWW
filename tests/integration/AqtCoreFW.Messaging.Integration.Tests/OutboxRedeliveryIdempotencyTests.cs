using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Messaging.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Messaging.Integration.Tests;

/// <summary>
/// Slice 1E — the consumer-side half of at-least-once correctness. An outbox redelivery (the SAME
/// logical event delivered ≥2×, which the custom <c>OutboxDispatcher</c> can produce — it republishes
/// a claimed row if it crashes between <c>Publish</c> and <c>MarkProcessed</c>, and a broker may also
/// redeliver) must be absorbed into a SINGLE effect by a consumer that claims a dedup key BEFORE its
/// effect.
///
/// Complements <see cref="OutboxAtLeastOnceDispatchTests"/> (outbox delivers at-least-once against real
/// SQL). Together the two prove the end-to-end guarantee: outbox delivers ≥1×, the ledger collapses it
/// to exactly-once effect. Distinct from <c>ProcessedMessageStoreTests</c> — that asserts the claim
/// primitive's boolean; here we assert the guard-then-effect CONSUMER PATTERN (the shape all three
/// Slice 1C consumers use) yields a single side-effect under duplicate delivery, against the real
/// <c>ProcessedMessageStore</c> + <c>messaging.processed_messages</c> table.
/// </summary>
[Collection(MessagingCollection.CollectionName)]
public sealed class OutboxRedeliveryIdempotencyTests(MessagingFixture fixture)
{
    // Mirrors the guard-then-effect shape of the Slice 1C consumers:
    //   if (!await store.TryClaimAsync(nameof(Consumer), dedupKey)) return;  // duplicate → skip
    //   <perform effect>
    // Each delivery runs in its own scope — the per-message consume scope in production.
    private async Task HandleWithLedgerGuardAsync(string consumer, string dedupKey, Action effect)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = new ProcessedMessageStore(
            scope.ServiceProvider.GetRequiredService<MessagingDbContext>());
        if (await store.TryClaimAsync(consumer, dedupKey))
            effect();
    }

    [Fact]
    public async Task DuplicateDelivery_OfSameEvent_RunsLedgerGuardedEffectExactlyOnce()
    {
        // Unique per run so parallel/rerun executions never collide on the shared DB.
        var consumer = $"OutboxRedeliverySpy_{Guid.NewGuid():N}";
        var dedupKey = Guid.NewGuid().ToString(); // stable per-message key carried in the event payload
        var effectCount = 0;

        // First delivery — claim wins, effect runs.
        await HandleWithLedgerGuardAsync(consumer, dedupKey, () => effectCount++);
        // Redelivery of the SAME event (at-least-once) — claim refused, effect skipped.
        await HandleWithLedgerGuardAsync(consumer, dedupKey, () => effectCount++);

        effectCount.Should().Be(1,
            "an at-least-once redelivery of the same event must produce exactly one guarded side-effect");
    }
}
