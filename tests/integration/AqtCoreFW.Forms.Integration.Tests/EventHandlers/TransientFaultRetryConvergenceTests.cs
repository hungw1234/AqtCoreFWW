using AqtCoreFW.Forms.Application.EventHandlers;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Forms.Integration.Tests.EventHandlers;

/// <summary>
/// The second distributed-fault test dimension (cell #10 slice-2): <b>timeout / transient-retry</b>.
/// The semantic complement of slice-1's duplication test — where that proves the claim-first
/// (<c>IProcessedMessageStore</c>) dedup strategy under an at-most-once consumer, this proves the
/// <b>effect-idempotent, throws-to-retry</b> strategy under a transient infra failure, driven through
/// the <b>real</b> MassTransit retry machinery.
///
/// Target: <see cref="WorkflowFinalizedFormSubmissionConsumer"/> — the purpose-built consumer that
/// deliberately does NOT use a ledger and instead lets infra exceptions propagate so
/// <c>UseMessageRetry</c> re-applies until success; exhausted retries dead-letter the message (a
/// visible <see cref="Fault{T}"/>), never a silent Pending strand (see the consumer's header :26-28).
///
/// Why this exists — the sibling unit test <c>WorkflowFinalizedFormSubmissionConsumerTests</c>
/// proves the consumer THROWS via a direct <c>Assert.ThrowsAsync</c> on a mocked
/// <see cref="ConsumeContext{T}"/>, but never drives real redelivery or dead-lettering. The
/// integration-level durability guarantee (a transient failure converges on retry; a persistent
/// failure surfaces a visible fault) was an unproven false-green until this slice
/// (memory <c>feedback-test-new-wiring-through-real-registration</c>).
///
/// The harness mirrors prod's bus (<c>MessagingRegistration.cs:64/101</c> — <c>UseMessageRetry</c> +
/// <c>ConfigureEndpoints</c>) but substitutes <c>Immediate</c> retry for prod's 5s/30s/5m backoff so
/// the test converges in-process. Minimal wiring (mocked <see cref="IFormSubmissionStatusClient"/> +
/// logging, no <c>TenantScopeConsumeFilter</c>, no SQL) matches slice-1 — the status client is a
/// substitute, so there is no ambient-tenant DB dependency to scope.
/// </summary>
public sealed class TransientFaultRetryConvergenceTests
{
    private static readonly TimeSpan MsgTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    [Trait("TestCase", "TC-FRM-INT-RETRY-001")]
    public async Task Consume_TransientFailureThenSuccess_RetriesUntilEffectApplies_NoFault()
    {
        var statusClient = Substitute.For<IFormSubmissionStatusClient>();

        // Transient infra failure on the FIRST application, success on the retry. The consumer lets the
        // exception propagate (it does not catch), so MassTransit's UseMessageRetry re-invokes Consume.
        var attempts = 0;
        statusClient
            .ApproveSubmissionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref attempts) == 1
                ? Task.FromException(new TimeoutException("form submission DB transiently unavailable"))
                : Task.CompletedTask);

        var (sp, harness) = BuildHarness(statusClient, retryCount: 3);
        await using (sp)
        {
            await harness.Start();
            try
            {
                var submissionId = Guid.NewGuid();
                await harness.Bus.Publish(ApprovedEvent(submissionId));

                // Wait for the retried (second) application to run — the convergence signal.
                await WaitUntilAsync(() => Volatile.Read(ref attempts) >= 2, MsgTimeout,
                    "the transient failure to be retried and the effect re-applied");

                // Effect applied exactly twice: one transient failure + one converged success. A consumer
                // that swallowed the failure would be Received(1) with no retry; this asserts the
                // propagate-then-retry contract actually fires end-to-end.
                await statusClient.Received(2).ApproveSubmissionAsync(
                    submissionId, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
                await statusClient.DidNotReceive().RejectSubmissionAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

                (await harness.Published.Any<Fault<WorkflowInstanceFinalizedIntegrationEvent>>(
                        x => x.Context.Message.Message.EntityId == submissionId))
                    .Should().BeFalse(because: "a retry that converges is not a fault — the decision was applied");
            }
            finally { await harness.Stop(); }
        }
    }

    [Fact]
    [Trait("TestCase", "TC-FRM-INT-RETRY-002")]
    public async Task Consume_PersistentFailure_ExhaustsRetries_PublishesVisibleFault()
    {
        var statusClient = Substitute.For<IFormSubmissionStatusClient>();

        // Persistent infra failure — every application throws. The decision must NOT vanish: exhausted
        // retries dead-letter the message as a visible Fault (the consumer's "never silently drop" guarantee).
        statusClient
            .ApproveSubmissionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("form submission DB persistently unavailable"));

        // Immediate(2) is chosen for a fast in-process exhaustion; prod (MessagingRegistration.cs:64)
        // retries 3 times. Received(3) below proves the exhaust→Fault MECHANISM, not prod's attempt count.
        var (sp, harness) = BuildHarness(statusClient, retryCount: 2);
        await using (sp)
        {
            await harness.Start();
            try
            {
                var submissionId = Guid.NewGuid();
                await harness.Bus.Publish(ApprovedEvent(submissionId));

                // Assert the fault FIRST — Any<Fault<T>> blocks until the fault is published (retries
                // exhausted) or the harness inactivity timeout elapses, so this also sequences the
                // Received(3) check below after all attempts have run.
                (await harness.Published.Any<Fault<WorkflowInstanceFinalizedIntegrationEvent>>(
                        x => x.Context.Message.Message.EntityId == submissionId))
                    .Should().BeTrue(because:
                        "once retries are exhausted the decision must surface as a visible Fault, " +
                        "never leave the submission silently Pending");

                // Initial delivery + 2 immediate retries = 3 failed applications.
                await statusClient.Received(3).ApproveSubmissionAsync(
                    submissionId, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            }
            finally { await harness.Stop(); }
        }
    }

    private static WorkflowInstanceFinalizedIntegrationEvent ApprovedEvent(Guid submissionId) =>
        new(
            InstancePublicId: Guid.NewGuid(),
            EntityType: "FormSubmission",
            EntityId: submissionId,
            FinalStateName: "Approved",
            TransitionedBy: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            MessageId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string description)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            if (cts.IsCancellationRequested)
                throw new TimeoutException(
                    $"Timed out after {timeout.TotalSeconds:0}s waiting for {description}.");
            await Task.Delay(25, CancellationToken.None);
        }
    }

    /// <summary>
    /// Builds a real in-memory MassTransit harness for the consumer, mirroring the prod bus shape
    /// (<c>UseMessageRetry</c> + <c>ConfigureEndpoints</c>) but with an <c>Immediate</c> retry policy so
    /// the test converges without prod's multi-second backoff.
    /// </summary>
    private static (ServiceProvider sp, ITestHarness harness) BuildHarness(
        IFormSubmissionStatusClient statusClient, int retryCount)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(statusClient);
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<WorkflowFinalizedFormSubmissionConsumer>();
            x.UsingInMemory((context, cfg) =>
            {
                cfg.UseMessageRetry(r => r.Immediate(retryCount));
                cfg.ConfigureEndpoints(context);
            });
        });
        var sp = services.BuildServiceProvider();
        var harness = sp.GetRequiredService<ITestHarness>();
        return (sp, harness);
    }
}
