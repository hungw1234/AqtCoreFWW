using AqtCoreFW.Notifications.Application.EventHandlers;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AqtCoreFW.Notifications.Integration.Tests.EventHandlers;

// TODO (SEC-2): when MassTransit moves to shared broker, consumer must validate
// evt.TenantId via principal context (cannot trust message-bus payload across
// process boundaries). Currently in-process only. See backlog SEC-2.

/// <summary>
/// Integration tests for <see cref="FormSubmissionAcknowledgedConsumer"/>.
/// Real MassTransit bus + DI wiring; per-test harness; no SQL, no seeders.
/// </summary>
public sealed class FormSubmissionAcknowledgedConsumerIntegrationTests
{
    private static readonly TimeSpan MsgTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    [Trait("TestCase", "TC-NTF-INT-003")]
    public async Task Consume_AuthSubmission_DispatchesNotification()
    {
        var mockClient = Substitute.For<INotificationModuleClient>();
        var (sp, harness) = BuildHarness(mockClient);
        await using (sp)
        {
            await harness.Start();
            try
            {
                var submissionId = Guid.NewGuid();
                var templateId = Guid.NewGuid();
                var tenantId = Guid.NewGuid();
                var submittedBy = Guid.NewGuid(); // R2 (H5): NON-EMPTY → consumer dispatches

                var evt = new FormSubmissionAcknowledgedIntegrationEvent(
                    SubmissionId: submissionId,
                    FormTemplateId: templateId,
                    TenantId: tenantId,
                    SubmittedBy: submittedBy,
                    MessageId: Guid.NewGuid(),
                    OccurredAt: DateTimeOffset.UtcNow);

                await harness.Bus.Publish(evt);

                using var cts = new CancellationTokenSource(MsgTimeout);
                (await harness.Consumed.Any<FormSubmissionAcknowledgedIntegrationEvent>(cts.Token))
                    .Should().BeTrue(because: "DI-registered consumer should receive the event");

                await mockClient.Received(1).SendAsync(
                    Arg.Is<SendNotificationRequest>(r =>
                        r.RecipientUserId == submittedBy &&
                        r.TenantId == tenantId &&
                        r.Subject == "Biểu mẫu đã được tiếp nhận" &&
                        r.Body == "Biểu mẫu của bạn đã được gửi thành công và đang chờ xử lý." &&
                        r.Channel == "inapp" &&
                        r.CorrelationId == submissionId.ToString()),
                    Arg.Any<CancellationToken>());
            }
            finally { await harness.Stop(); }
        }
    }

    [Fact]
    [Trait("TestCase", "TC-NTF-INT-004")]
    public async Task Consume_AnonSubmission_SkipsDispatch()
    {
        var mockClient = Substitute.For<INotificationModuleClient>();
        var (sp, harness) = BuildHarness(mockClient);
        await using (sp)
        {
            await harness.Start();
            try
            {
                var evt = new FormSubmissionAcknowledgedIntegrationEvent(
                    SubmissionId: Guid.NewGuid(),
                    FormTemplateId: Guid.NewGuid(),
                    TenantId: Guid.NewGuid(),
                    SubmittedBy: Guid.Empty, // R2 (H5): triggers anon early-return
                    MessageId: Guid.NewGuid(),
                    OccurredAt: DateTimeOffset.UtcNow);

                await harness.Bus.Publish(evt);

                using var cts = new CancellationTokenSource(MsgTimeout);
                (await harness.Consumed.Any<FormSubmissionAcknowledgedIntegrationEvent>(cts.Token))
                    .Should().BeTrue(because: "consumer should still receive the message");

                await mockClient.DidNotReceive().SendAsync(
                    Arg.Any<SendNotificationRequest>(),
                    Arg.Any<CancellationToken>());

                (await harness.Published.Any<Fault<FormSubmissionAcknowledgedIntegrationEvent>>())
                    .Should().BeFalse(because: "anon-skip path must not produce a Fault<T>");
            }
            finally { await harness.Stop(); }
        }
    }

    private static (ServiceProvider sp, ITestHarness harness) BuildHarness(
        INotificationModuleClient mockClient, IProcessedMessageStore? dedup = null)
    {
        // Default store claims every message (first delivery). A test that exercises the duplicate path
        // passes its own substitute returning false.
        dedup ??= CreateClaimingStore(claims: true);

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

    private static IProcessedMessageStore CreateClaimingStore(bool claims)
    {
        var store = Substitute.For<IProcessedMessageStore>();
        store.TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(claims);
        return store;
    }

    [Fact]
    [Trait("TestCase", "TC-NTF-INT-005")]
    public async Task Consume_DuplicateDelivery_SkipsNotification()
    {
        var mockClient = Substitute.For<INotificationModuleClient>();
        // Store returns false → this message was already processed → consumer must not dispatch.
        var (sp, harness) = BuildHarness(mockClient, CreateClaimingStore(claims: false));
        await using (sp)
        {
            await harness.Start();
            try
            {
                var evt = new FormSubmissionAcknowledgedIntegrationEvent(
                    SubmissionId: Guid.NewGuid(),
                    FormTemplateId: Guid.NewGuid(),
                    TenantId: Guid.NewGuid(),
                    SubmittedBy: Guid.NewGuid(), // non-empty → would dispatch if not a duplicate
                    MessageId: Guid.NewGuid(),
                    OccurredAt: DateTimeOffset.UtcNow);

                await harness.Bus.Publish(evt);

                using var cts = new CancellationTokenSource(MsgTimeout);
                (await harness.Consumed.Any<FormSubmissionAcknowledgedIntegrationEvent>(cts.Token))
                    .Should().BeTrue(because: "consumer still receives the message");

                await mockClient.DidNotReceive().SendAsync(
                    Arg.Any<SendNotificationRequest>(), Arg.Any<CancellationToken>());
            }
            finally { await harness.Stop(); }
        }
    }
}
