using System.Collections.Concurrent;
using AqtCoreFW.Notifications.Application.EventHandlers;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AqtCoreFW.Notifications.Integration.Tests.EventHandlers;

/// <summary>
/// The first distributed-fault test dimension: message duplication. Mirrors the arrange/harness
/// pattern of
/// <see cref="FormSubmissionAcknowledgedConsumerIntegrationTests"/> (same real MassTransit bus +
/// DI wiring; per-test harness; no SQL, no seeders) but, unlike that sibling's
/// <c>Consume_DuplicateDelivery_SkipsNotification</c> test — which substitutes
/// <see cref="IProcessedMessageStore"/> to CANNED-return <c>false</c> regardless of call count —
/// this test uses a genuine claim-first in-memory store so the SAME <see cref="SubmissionId"/> is
/// published TWICE through the real bus and the dedup gate is exercised for real, not simulated.
///
/// Target: <see cref="FormSubmissionAcknowledgedConsumer"/> — verified at-most-once (swallow-effect)
/// claim-first consumer (dedups on SubmissionId via IProcessedMessageStore.TryClaimAsync, :50).
/// Deliberately NOT WorkflowFinalizedFormSubmissionConsumer — that consumer is idempotent BY
/// EFFECT and intentionally does not use the ledger; asserting claim-first dedup against it would
/// be the wrong semantics — a claim-first ledger assertion only fits an at-most-once (swallow)
/// consumer, never an effect-idempotent one.
/// </summary>
public sealed class MessageDuplicationIdempotencyTests
{
    private static readonly TimeSpan MsgTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    [Trait("TestCase", "TC-NTF-INT-006")]
    public async Task Consume_DuplicateDeliverySameSubmissionId_EffectDispatchedExactlyOnce()
    {
        var mockClient = Substitute.For<INotificationModuleClient>();
        var dedup = new ClaimFirstProcessedMessageStore();
        var (sp, harness) = BuildHarness(mockClient, dedup);
        await using (sp)
        {
            await harness.Start();
            try
            {
                var submissionId = Guid.NewGuid();
                var templateId = Guid.NewGuid();
                var tenantId = Guid.NewGuid();
                var submittedBy = Guid.NewGuid(); // authenticated — anon Guid.Empty early-returns before claiming (:34-35)

                // The literal SAME event delivered twice — an at-least-once redelivery. Per
                // IProcessedMessageStore's own doc, the outbox dispatcher "does not pin a stable
                // transport MessageId", so this consumer must dedup on SubmissionId, not on any
                // MassTransit-level message identity — publishing the identical payload twice
                // models that redelivery faithfully.
                var evt = new FormSubmissionAcknowledgedIntegrationEvent(
                    SubmissionId: submissionId,
                    FormTemplateId: templateId,
                    TenantId: tenantId,
                    SubmittedBy: submittedBy,
                    MessageId: Guid.NewGuid(),
                    OccurredAt: DateTimeOffset.UtcNow);

                await harness.Bus.Publish(evt);
                await harness.Bus.Publish(evt); // duplicate delivery of the identical message

                // Wait for both deliveries to be consumed (not merely for "any" — Any<T>() with a
                // SubmissionId predicate would already be satisfied by the first delivery alone).
                var consumedCount = await WaitForConsumedCountAsync(harness, submissionId, expectedCount: 2, MsgTimeout);
                consumedCount.Should().Be(2,
                    because: "both deliveries must be consumed by the consumer, not dropped or faulted");

                (await harness.Published.Any<Fault<FormSubmissionAcknowledgedIntegrationEvent>>(
                        x => x.Context.Message.Message.SubmissionId == submissionId))
                    .Should().BeFalse(because: "a claim-first skip is a normal early return, not a fault");

                await mockClient.Received(1).SendAsync(
                    Arg.Is<SendNotificationRequest>(r =>
                        r.RecipientUserId == submittedBy &&
                        r.TenantId == tenantId &&
                        r.CorrelationId == submissionId.ToString()),
                    Arg.Any<CancellationToken>());

                dedup.ClaimCount.Should().Be(1,
                    because: "the claim-first ledger records the (consumer, SubmissionId) pair exactly once " +
                              "regardless of delivery count — this is the secondary, ledger-level confirmation " +
                              "of the primary effect-count assertion above");
            }
            finally { await harness.Stop(); }
        }
    }

    private static async Task<int> WaitForConsumedCountAsync(
        ITestHarness harness, Guid submissionId, int expectedCount, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            var count = harness.Consumed
                .Select<FormSubmissionAcknowledgedIntegrationEvent>()
                .Count(x => x.Context.Message.SubmissionId == submissionId);
            if (count >= expectedCount) return count;
            await Task.Delay(25, CancellationToken.None);
        }

        return harness.Consumed
            .Select<FormSubmissionAcknowledgedIntegrationEvent>()
            .Count(x => x.Context.Message.SubmissionId == submissionId);
    }

    private static (ServiceProvider sp, ITestHarness harness) BuildHarness(
        INotificationModuleClient mockClient, IProcessedMessageStore dedup)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mockClient);
        services.AddSingleton(dedup);
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<FormSubmissionAcknowledgedConsumer>();
        });
        var sp = services.BuildServiceProvider();
        var harness = sp.GetRequiredService<ITestHarness>();
        return (sp, harness);
    }

    /// <summary>
    /// Genuine claim-first <see cref="IProcessedMessageStore"/> — the FIRST call for a given
    /// (consumer, key) pair claims (returns true); every subsequent call for the SAME pair is
    /// refused (returns false). Unlike a canned-return substitute, this exercises the actual
    /// dedup gate rather than asserting against a hardcoded outcome (see class doc).
    /// </summary>
    private sealed class ClaimFirstProcessedMessageStore : IProcessedMessageStore
    {
        private readonly ConcurrentDictionary<(string Consumer, string Key), byte> _claims = new();

        /// <summary>Number of (consumer, key) pairs that were ever granted a first-time claim.</summary>
        public int ClaimCount => _claims.Count;

        public Task<bool> TryClaimAsync(string consumerName, string dedupKey, CancellationToken ct = default)
            => Task.FromResult(_claims.TryAdd((consumerName, dedupKey), 0));
    }
}
