using System.Text.Json;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Outbox;

/// <summary>
/// Unit tests for <see cref="OutboxDispatcher"/>.
/// Mocks <see cref="IOutboxStore"/> and MassTransit <see cref="IBus"/> to exercise
/// dispatch outcomes (Dispatched / Retry / DeadLettered) without touching SQL.
/// </summary>
public sealed class OutboxDispatcherTests
{
    private const string Schema = "cases";

    /// <summary>Not an IExternalDomainEvent — used for tests that go through the resolver's
    /// legacy AssemblyQualifiedName fallback (rows written before the stable-token cutover).</summary>
    public sealed record TestEvent(Guid CaseId, Guid TenantId);

    /// <summary>A REAL IExternalDomainEvent implementer, used to prove the production
    /// dispatch path: EventType = bare Type.FullName, resolved via
    /// OutboxEventTypeResolver's FullName map — not the AQN fallback.</summary>
    public sealed record ExternalTestEvent(Guid MessageId) : IExternalDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private static OutboxRow BuildRow(int retryCount = 0, string? eventType = null, string? payload = null)
    {
        var typeName = eventType ?? typeof(TestEvent).AssemblyQualifiedName!;
        var json = payload ?? JsonSerializer.Serialize(new TestEvent(Guid.NewGuid(), Guid.NewGuid()));
        return new OutboxRow(Guid.NewGuid(), typeName, json, retryCount);
    }

    private static (OutboxDispatcher dispatcher, IOutboxStore store, IBus bus, IOutboxSchemaRegistry registry)
        BuildDispatcher(OutboxOptions? overrideOpts = null, IReadOnlyList<string>? schemas = null)
    {
        var store = Substitute.For<IOutboxStore>();
        var bus = Substitute.For<IBus>();
        var registry = Substitute.For<IOutboxSchemaRegistry>();
        registry.Schemas.Returns(schemas ?? new[] { Schema });

        var options = Options.Create(overrideOpts ?? new OutboxOptions
        {
            EnableDispatcher = true,
            BatchSize = 50,
            DeadLetterAfterRetries = 3
        });

        // Real resolver, not a substitute — it's pure (no I/O) and this exercises the actual
        // FullName + legacy-AQN resolution path the same way production wiring does.
        var dispatcher = new OutboxDispatcher(
            store, bus, registry, new OutboxEventTypeResolver(), options, NullLogger<OutboxDispatcher>.Instance);

        return (dispatcher, store, bus, registry);
    }

    [Fact]
    public async Task DispatchBatch_WhenDisabled_ShortCircuitsAndDoesNotTouchStore()
    {
        var (dispatcher, store, bus, _) = BuildDispatcher(
            new OutboxOptions { EnableDispatcher = false });

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        summary.Should().Be(OutboxDispatchSummary.Empty);
        await store.DidNotReceiveWithAnyArgs().ClaimBatchAsync(default!, default, default);
        await bus.DidNotReceiveWithAnyArgs().Publish(default(object)!, default(Type)!, default);
    }

    [Fact]
    public async Task DispatchBatch_WhenNoSchemasRegistered_ReturnsEmptyAndSkipsStore()
    {
        var (dispatcher, store, bus, _) = BuildDispatcher(schemas: Array.Empty<string>());

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        summary.Should().Be(OutboxDispatchSummary.Empty);
        await store.DidNotReceiveWithAnyArgs().ClaimBatchAsync(default!, default, default);
        await bus.DidNotReceiveWithAnyArgs().Publish(default(object)!, default(Type)!, default);
    }

    [Fact]
    public async Task DispatchBatch_HappyPath_PublishesAndMarksProcessed()
    {
        var (dispatcher, store, bus, _) = BuildDispatcher();
        var row = BuildRow();
        store.ClaimBatchAsync(Schema, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { row });

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        summary.Should().Be(new OutboxDispatchSummary(1, 0, 0));
        await bus.Received(1).Publish(
            Arg.Is<object>(o => o is TestEvent), typeof(TestEvent), Arg.Any<CancellationToken>());
        await store.Received(1).MarkProcessedAsync(Schema, row.Id, Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MarkRetryAsync(default!, default, default, default!, default);
        await store.DidNotReceiveWithAnyArgs().MoveToDeadLetterAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task DispatchBatch_WhenEventTypeIsBareFullNameOfRealExternalEvent_ResolvesViaMapAndPublishes()
    {
        // Production write shape: OutboxInterceptor writes evt.GetType().FullName! — a bare
        // token, with NO assembly qualification. This must resolve via
        // OutboxEventTypeResolver's FullName map (built from a real IExternalDomainEvent
        // implementer), not the AssemblyQualifiedName fallback — Type.GetType(bareName) can
        // only ever find a type in the calling assembly + corelib, so if this test only
        // passed via the fallback it would mean the map path is broken/untested.
        var (dispatcher, store, bus, _) = BuildDispatcher();
        var row = BuildRow(
            eventType: typeof(ExternalTestEvent).FullName!,
            payload: JsonSerializer.Serialize(new ExternalTestEvent(Guid.NewGuid())));
        store.ClaimBatchAsync(Schema, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { row });

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        summary.Should().Be(new OutboxDispatchSummary(1, 0, 0));
        await bus.Received(1).Publish(
            Arg.Is<object>(o => o is ExternalTestEvent), typeof(ExternalTestEvent), Arg.Any<CancellationToken>());
        await store.Received(1).MarkProcessedAsync(Schema, row.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBatch_WhenEventTypeUnresolvable_DeadLettersImmediately()
    {
        var (dispatcher, store, bus, _) = BuildDispatcher();
        var row = BuildRow(eventType: "Some.Renamed.Or.Removed.Type, OldAssembly");
        store.ClaimBatchAsync(Schema, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { row });

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        summary.Should().Be(new OutboxDispatchSummary(0, 0, 1));
        await store.Received(1).MoveToDeadLetterAsync(
            Schema, row, Arg.Is<string>(s => s.Contains("is unresolvable")),
            Arg.Any<CancellationToken>());
        await bus.DidNotReceiveWithAnyArgs().Publish(default(object)!, default(Type)!, default);
        await store.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default!, default, default);
    }

    [Fact]
    public async Task DispatchBatch_WhenPublishFailsAndRetriesRemain_MarksRetry()
    {
        var (dispatcher, store, bus, _) = BuildDispatcher();
        var row = BuildRow(retryCount: 0); // 0+1=1 < 3 threshold
        store.ClaimBatchAsync(Schema, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { row });
        bus.Publish(Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Bus is down"));

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        summary.Should().Be(new OutboxDispatchSummary(0, 1, 0));
        await store.Received(1).MarkRetryAsync(
            Schema, row.Id, 1, Arg.Is<string>(e => e.Contains("Bus is down")),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MoveToDeadLetterAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task DispatchBatch_WhenPublishFailsAndBudgetExhausted_DeadLetters()
    {
        var (dispatcher, store, bus, _) = BuildDispatcher();
        var row = BuildRow(retryCount: 2); // 2+1=3 == DeadLetterAfterRetries
        store.ClaimBatchAsync(Schema, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { row });
        bus.Publish(Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("publish timed out"));

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        summary.Should().Be(new OutboxDispatchSummary(0, 0, 1));
        await store.Received(1).MoveToDeadLetterAsync(
            Schema, row, Arg.Is<string>(e => e.Contains("publish timed out")),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MarkRetryAsync(default!, default, default, default!, default);
    }

    [Fact]
    public async Task DispatchBatch_WhenJsonDeserialiseToNull_TreatsAsFailureAndRetries()
    {
        var (dispatcher, store, bus, _) = BuildDispatcher();
        // JSON literal "null" deserialises to null payload object → handler treats as failure.
        var row = BuildRow(payload: "null");
        store.ClaimBatchAsync(Schema, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { row });

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        summary.Retried.Should().Be(1);
        await bus.DidNotReceiveWithAnyArgs().Publish(default(object)!, default(Type)!, default);
        await store.Received(1).MarkRetryAsync(
            Schema, row.Id, 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBatch_WhenStoreClaimThrows_LogsAndContinuesOtherSchemas()
    {
        var (dispatcher, store, bus, _) = BuildDispatcher(
            schemas: new[] { "cases", "workflow" });

        store.ClaimBatchAsync("cases", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("transient db blip"));

        var workflowRow = BuildRow();
        store.ClaimBatchAsync("workflow", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { workflowRow });

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        // First schema swallowed; second schema should still process.
        summary.Dispatched.Should().Be(1);
        await store.Received(1).MarkProcessedAsync("workflow", workflowRow.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBatch_WhenBatchEmpty_DoesNotInvokeBus()
    {
        var (dispatcher, store, bus, _) = BuildDispatcher();
        store.ClaimBatchAsync(Schema, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OutboxRow>());

        var summary = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        summary.Should().Be(OutboxDispatchSummary.Empty);
        await bus.DidNotReceiveWithAnyArgs().Publish(default(object)!, default(Type)!, default);
    }
}
