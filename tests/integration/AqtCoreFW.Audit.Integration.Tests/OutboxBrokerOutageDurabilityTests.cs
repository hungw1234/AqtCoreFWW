using System.Runtime.CompilerServices;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Audit.Integration.Tests;

/// <summary>
/// Cell #10 (Resilience) slice-3 — the <b>partition / broker-outage</b> distributed-fault dimension.
/// Semantic complement of slice-2 (<c>TransientFaultRetryConvergenceTests</c>, which drove the MassTransit
/// consumer-side retry machinery). This slice proves the <b>durable transactional-outbox relay</b>
/// (<see cref="OutboxDispatcher"/>, driven in prod by the <see cref="OutboxProcessor"/> hosted service)
/// survives a message-broker outage WITHOUT losing a message:
///
///   Test A — a transient outage retains the pending row (never marks it processed, never drops it) and
///            converges to Dispatched once the broker recovers → zero message loss across a partition.
///   Test B — a persistent outage exhausts the retry budget into a VISIBLE dead-letter row
///            (<c>messaging.dead_letters</c>), never a silent drop.
///
/// Broker outage is simulated by an <see cref="IBus"/> substitute whose <c>Publish</c> throws while an
/// outage flag is set; the dispatcher is resolved through the REAL DI registration
/// (<c>AddScoped&lt;OutboxDispatcher&gt;()</c>) so the production wiring — not a hand-rolled dispatcher — is
/// under test. Uses the REAL SQL Server integration DB (<see cref="WebAppFixture"/>): durability is a
/// row-state-transition property that an in-memory EF provider cannot express.
///
/// Blast radius: <c>OutboxDispatcher.DispatchBatchAsync</c> claims pending rows from EVERY registered
/// schema, not just <c>audit</c>. Under the default one-DB-per-assembly Testcontainers mode (and the
/// serialized <see cref="SqlServerCollection"/>) that is contained; a future shared-DB cross-assembly mode
/// (<c>TEST_USE_LOCAL_SQL=true</c>) would let this faulting-bus cycle retry/dead-letter another assembly's
/// in-flight rows. Every assertion here filters on the unique per-test <c>tenantId</c>, so this test is
/// immune to bystanders; the caution is only for the reverse direction.
///
/// RLS posture: the outbox store is tenant-agnostic (dedicated ADO.NET connection, no SESSION_CONTEXT;
/// <c>audit.OutboxMessages</c> and <c>messaging.dead_letters</c> carry no RLS policy), so this is a
/// gate-tier durability proof, not an RLS-tier isolation proof — the sa/db_owner false-green concern does
/// not apply. <c>tenantId</c> is used only as a unique row marker, never as an isolation boundary.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class OutboxBrokerOutageDurabilityTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public OutboxBrokerOutageDurabilityTests(WebAppFixture app) => _app = app;

    // --- Test A: transient outage → retry retained → recovery converges, no message loss ---

    [Fact]
    public async Task TransientBrokerOutageThenRecovery_RetriesThenDispatches_NoLoss()
    {
        var tenantId = Guid.NewGuid();
        var action = $"OutageTransient{Guid.NewGuid():N}";
        var (app, bus, setOutage) = BuildFaultingBusApp();

        await SeedOnePendingAuditOutboxRow(app, tenantId, action);

        // Outage active: every publish fails → the seeded row must be RETAINED as a retry, not lost.
        var s1 = await DispatchOnce(app);
        s1.Dispatched.Should().Be(0, "every publish fails during a full broker outage");
        s1.Retried.Should().BeGreaterThanOrEqualTo(1, "the seeded row is retried, not dropped");

        var row1 = await ReadOutboxRow(tenantId);
        row1.ProcessedAtUtc.Should().BeNull("the row is retained (unprocessed) across the outage — never lost");
        row1.RetryCount.Should().Be(1, "exactly one failed dispatch cycle → RetryCount incremented once");

        // Non-vacuous: prove the dispatcher actually reached the publish seam during the outage.
        await bus.Received().Publish(Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());

        // Broker recovers: the next cycle converges the retained row to Dispatched.
        setOutage(false);
        var s2 = await DispatchOnce(app);
        s2.Dispatched.Should().BeGreaterThanOrEqualTo(1, "the retained row publishes once the broker is back");

        var row2 = await ReadOutboxRow(tenantId);
        row2.ProcessedAtUtc.Should().NotBeNull(
            "converged — the row is dispatched after recovery, so a broker partition caused zero message loss");
    }

    // --- Test B: persistent outage → retries exhaust → visible dead-letter, never a silent drop ---

    [Fact]
    public async Task PersistentBrokerOutage_ExhaustsRetries_DeadLettersVisibly_NoSilentDrop()
    {
        var tenantId = Guid.NewGuid();
        var action = $"OutagePersistent{Guid.NewGuid():N}";
        // DeadLetterAfterRetries = 2 → bounded, deterministic: cycle 1 retries, cycle 2 dead-letters.
        var (app, _, _) = BuildFaultingBusApp(deadLetterAfter: 2);

        await SeedOnePendingAuditOutboxRow(app, tenantId, action);

        // Cycle 1: newRetryCount 1 >= 2? no → retry. Row retained, one visible retry recorded.
        await DispatchOnce(app);
        var afterCycle1 = await ReadOutboxRow(tenantId);
        afterCycle1.RetryCount.Should().Be(1, "the first failure is a retry, not yet a dead-letter");
        afterCycle1.ProcessedAtUtc.Should().BeNull("still pending after one retry");

        // Cycle 2: newRetryCount 2 >= 2? yes → dead-letter (visible), row leaves the pending set.
        await DispatchOnce(app);

        var deadLetters = await ReadDeadLetters(tenantId);
        deadLetters.Should().HaveCount(1,
            "an exhausted outbox row is dead-lettered VISIBLY into messaging.dead_letters, never silently dropped");
        deadLetters[0].MessageType.Should().Be(
            typeof(AuditLogAppendedIntegrationEvent).FullName,
            "the dead letter is attributable to the original event contract");
        deadLetters[0].OriginalQueue.Should().Be("outbox:audit",
            "the dead letter names its source outbox schema");

        var afterCycle2 = await ReadOutboxRow(tenantId);
        afterCycle2.ProcessedAtUtc.Should().NotBeNull(
            "dead-lettered rows are marked processed — they leave the pending set INTO the DLQ, not into the void");
    }

    // --- Harness -----------------------------------------------------------------------------------

    /// <summary>
    /// Clones the fixture host with an <see cref="IBus"/> substitute standing in for the broker: while the
    /// outage flag is set, every <c>Publish</c> throws. The dispatcher is left registered as the real
    /// <c>AddScoped&lt;OutboxDispatcher&gt;()</c>; only the bus is swapped. Optionally overrides
    /// <see cref="OutboxOptions.DeadLetterAfterRetries"/> for a bounded exhaustion count.
    /// </summary>
    private (WebApplicationFactory<AqtCoreFW.Api.Program> App, IBus Bus, Action<bool> SetOutage)
        BuildFaultingBusApp(int? deadLetterAfter = null)
    {
        // StrongBox so the flag is shared by reference between the substitute closure and the setter.
        var outage = new StrongBox<bool>(true);

        var bus = Substitute.For<IBus>();
        bus.Publish(Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>())
            .Returns(_ => outage.Value
                ? Task.FromException(new InvalidOperationException("simulated broker outage (test)"))
                : Task.CompletedTask);

        var app = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBus>();
                services.AddSingleton(bus);
                services.PostConfigure<OutboxOptions>(o =>
                {
                    // Anti-starvation guard: the seeded row has the NEWEST CreatedAtUtc, and ClaimBatchAsync
                    // orders ASC and takes TOP(BatchSize). Pending audit outbox rows accumulate across this
                    // serialized collection (the OutboxProcessor that would drain them is stripped in tests),
                    // so the default BatchSize=50 could eventually push the newest seeded row out of every
                    // batch → never claimed → RetryCount stays 0 and both facts flake. Bump well past
                    // realistic suite accumulation so the seeded row is always claimable.
                    o.BatchSize = 1000;
                    if (deadLetterAfter is int n)
                        o.DeadLetterAfterRetries = n;
                });
            }));

        return (app, bus, v => outage.Value = v);
    }

    /// <summary>
    /// Seeds exactly one PENDING audit outbox row via the real audit write path (mirrors
    /// <c>AuditOutboxAtomicityTests</c>). The write rides <c>IOutboxWriter</c>, not <see cref="IBus"/>, so
    /// the faulting bus never interferes with seeding. <paramref name="tenantId"/> lands in the payload and
    /// is the unique marker every read/assert filters on (isolating this test from any other schema's rows).
    /// </summary>
    private static async Task SeedOnePendingAuditOutboxRow(
        WebApplicationFactory<AqtCoreFW.Api.Program> app, Guid tenantId, string action)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        await auditLogger.LogCommandAsync(
            action, "TestModule", "TestResource",
            resourceId: "res-1", commandType: null,
            dataSnapshot: "SHOULD_NOT_CROSS",
            tenantIdOverride: tenantId);
    }

    private async Task<OutboxDispatchSummary> DispatchOnce(WebApplicationFactory<AqtCoreFW.Api.Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        return await dispatcher.DispatchBatchAsync(CancellationToken.None);
    }

    private async Task<OutboxMessage> ReadOutboxRow(Guid tenantId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Payload.Contains(tenantId.ToString()));
    }

    private async Task<List<MessageDeadLetter>> ReadDeadLetters(Guid tenantId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        return await db.DeadLetters.AsNoTracking()
            .Where(d => d.OriginalQueue == "outbox:audit" && d.Payload.Contains(tenantId.ToString()))
            .ToListAsync();
    }
}
