using AqtCoreFW.Notifications.Application.EventHandlers;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Notifications.Application.Tests.EventHandlers;

/// <summary>
/// Unit tests for FormSubmissionAcknowledgedConsumer (260427-1943 Phase-03). Mirrors the
/// CaseTransitionedNotificationConsumer test shape. Mocks ConsumeContext directly — pure
/// message-handling assertions, no MassTransit harness, no DB.
/// </summary>
public sealed class FormSubmissionAcknowledgedConsumerTests
{
    private readonly INotificationModuleClient _client = Substitute.For<INotificationModuleClient>();
    private readonly IProcessedMessageStore _dedup = Substitute.For<IProcessedMessageStore>();
    private readonly ILogger<FormSubmissionAcknowledgedConsumer> _logger =
        Substitute.For<ILogger<FormSubmissionAcknowledgedConsumer>>();
    private readonly FormSubmissionAcknowledgedConsumer _sut;

    private static readonly Guid SubmissionId = Guid.NewGuid();
    private static readonly Guid TemplateId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SubmitterId = Guid.NewGuid();

    public FormSubmissionAcknowledgedConsumerTests()
    {
        // Default: first-time claim succeeds (NSubstitute would otherwise return false → skip).
        _dedup.TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new FormSubmissionAcknowledgedConsumer(_client, _dedup, _logger);
    }

    private static ConsumeContext<FormSubmissionAcknowledgedIntegrationEvent> Context(Guid submittedBy)
    {
        var ctx = Substitute.For<ConsumeContext<FormSubmissionAcknowledgedIntegrationEvent>>();
        ctx.Message.Returns(new FormSubmissionAcknowledgedIntegrationEvent(
            SubmissionId, TemplateId, TenantId, submittedBy, Guid.NewGuid(), DateTimeOffset.UtcNow));
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consume_AuthenticatedSubmission_DispatchesInAppAck()
    {
        await _sut.Consume(Context(SubmitterId));

        await _client.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.RecipientUserId == SubmitterId &&
                r.TenantId == TenantId &&
                r.Channel == "inapp" &&
                r.CorrelationId == SubmissionId.ToString() &&
                r.Subject == "Biểu mẫu đã được tiếp nhận"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_AnonymousPublicSubmission_SkipsDispatch()
    {
        // SubmittedBy = Guid.Empty signals anonymous public submission — no recipient to notify.
        await _sut.Consume(Context(Guid.Empty));

        await _client.DidNotReceive().SendAsync(
            Arg.Any<SendNotificationRequest>(), Arg.Any<CancellationToken>());
        // The anon early-return is BEFORE the claim → no ledger row for a no-effect path.
        await _dedup.DidNotReceive().TryClaimAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_SkipsDispatch()
    {
        // Slice 1C: a redelivery whose SubmissionId was already claimed must not send a 2nd notification.
        _dedup.TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await _sut.Consume(Context(SubmitterId));

        await _client.DidNotReceive().SendAsync(
            Arg.Any<SendNotificationRequest>(), Arg.Any<CancellationToken>());
        await _dedup.Received(1).TryClaimAsync(
            nameof(FormSubmissionAcknowledgedConsumer), SubmissionId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_NotificationClientThrows_SwallowsAndLogs()
    {
        // Fire-and-log: throwing would replay via MassTransit retry; we prefer lossy behavior
        // over a retry storm against a degraded Notifications dependency.
        _client.SendAsync(Arg.Any<SendNotificationRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("transient outage"));

        var act = async () => await _sut.Consume(Context(SubmitterId));

        await act.Should().NotThrowAsync();
    }
}
