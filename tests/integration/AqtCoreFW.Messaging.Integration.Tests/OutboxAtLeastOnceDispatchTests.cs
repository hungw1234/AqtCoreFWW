using AqtCoreFW.Messaging.Integration.Tests.Fixtures;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Messaging.Integration.Tests;

/// <summary>
/// Slice 1E — the outbox-at-least-once headline proof, WHITE-BOX against real SQL.
///
/// Drives the production <see cref="AqtCoreFW.Infrastructure.Persistence.Outbox.OutboxDispatcher"/>
/// over the real <c>OutboxStore</c> (via <see cref="OutboxStoreFixture"/>) with a fault-injecting
/// <see cref="IBus"/> spy — no broker container, fully deterministic. This exercises the
/// persist→claim→publish→mark interplay end-to-end: the mocked <c>OutboxDispatcherTests</c> proves
/// the dispatch-outcome LOGIC in isolation, but only a real Store round-trip proves that a
/// publish-fault genuinely LEAVES the row pending in SQL and that the NEXT cycle re-claims and
/// dispatches it (the at-least-once guarantee).
///
/// The consumer-side dedup half (a redelivery absorbed into a single effect) is proven separately
/// against the real <c>ProcessedMessageStore</c> in <see cref="OutboxRedeliveryIdempotencyTests"/>.
/// </summary>
[Collection(OutboxStoreCollection.CollectionName)]
public sealed class OutboxAtLeastOnceDispatchTests
{
    private readonly OutboxStoreFixture _fx;

    public OutboxAtLeastOnceDispatchTests(OutboxStoreFixture fx) => _fx = fx;

    /// <summary>A real IExternalDomainEvent implementer so the resolver's FullName map dispatches it
    /// (mirrors the proven <c>ExternalTestEvent</c> shape from the unit suite).</summary>
    public sealed record RedispatchTestEvent(Guid MessageId, Guid TenantId) : IExternalDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    [Fact]
    public async Task Dispatch_HappyPath_PublishesAndMarksRowProcessed_AgainstRealSql()
    {
        await _fx.TruncateAsync();
        var id = await _fx.SeedResolvableRowAsync(new RedispatchTestEvent(Guid.NewGuid(), Guid.NewGuid()));

        var bus = Substitute.For<IBus>();
        var summary = await _fx.BuildDispatcher(bus).DispatchBatchAsync(CancellationToken.None);

        summary.Dispatched.Should().Be(1);
        await bus.Received(1).Publish(
            Arg.Is<object>(o => o is RedispatchTestEvent), typeof(RedispatchTestEvent),
            Arg.Any<CancellationToken>());

        var state = await _fx.ReadRowStateAsync(id);
        state.ProcessedAtUtc.Should().NotBeNull("a dispatched row is marked processed in SQL");
        state.ClaimedAtUtc.Should().BeNull("MarkProcessed releases the claim");
    }

    [Fact]
    public async Task BrokerDown_RowSurvivesInSql_ThenRedispatchesWhenBrokerRecovers()
    {
        await _fx.TruncateAsync();
        var evt = new RedispatchTestEvent(Guid.NewGuid(), Guid.NewGuid());
        var id = await _fx.SeedResolvableRowAsync(evt);

        // --- Cycle 1: broker DOWN — Publish throws. Row must SURVIVE (not lost, not dead-lettered). ---
        var downBus = Substitute.For<IBus>();
        downBus.Publish(Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("broker unreachable"));

        var faultSummary = await _fx.BuildDispatcher(downBus).DispatchBatchAsync(CancellationToken.None);

        faultSummary.Retried.Should().Be(1, "one publish fault under the dead-letter threshold retries, not drops");
        faultSummary.Dispatched.Should().Be(0);
        var afterFault = await _fx.ReadRowStateAsync(id);
        afterFault.ProcessedAtUtc.Should().BeNull("a broker outage must NOT lose the event — the row stays pending");
        afterFault.RetryCount.Should().Be(1);
        afterFault.ClaimedAtUtc.Should().BeNull(
            "MarkRetry releases the claim so the row is immediately re-eligible next cycle (not gated by stale-timeout)");

        // --- Cycle 2: broker UP — same row re-claimed and dispatched (at-least-once delivered). ---
        var upBus = Substitute.For<IBus>();
        var recoverSummary = await _fx.BuildDispatcher(upBus).DispatchBatchAsync(CancellationToken.None);

        recoverSummary.Dispatched.Should().Be(1, "the surviving row is redispatched once the broker recovers");
        // Expression-tree matcher: `o is T` (TypeIs) + cast is allowed; a declaration pattern
        // (`o is T e`) would trip CS8122, so cast-then-compare to keep the identity assertion.
        await upBus.Received(1).Publish(
            Arg.Is<object>(o => o is RedispatchTestEvent && ((RedispatchTestEvent)o).MessageId == evt.MessageId),
            typeof(RedispatchTestEvent), Arg.Any<CancellationToken>());

        var afterRecovery = await _fx.ReadRowStateAsync(id);
        afterRecovery.ProcessedAtUtc.Should().NotBeNull("the redispatched row is finally marked processed");
    }
}
