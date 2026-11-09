using AqtCoreFW.Forms.Application.EventHandlers;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.EventHandlers;

/// <summary>
/// Unit tests for <see cref="WorkflowFinalizedFormSubmissionConsumer"/> — the outbox-routed replacement
/// for the removed in-process WorkflowTransitionedFormSubmissionHandler. Mocks ConsumeContext directly;
/// no MassTransit harness, no DB. Covers the state→approve/reject mapping, unrecognized-state no-op,
/// the non-FormSubmission guard, and the retry-durability property (infra failure MUST propagate so
/// MassTransit retries — the consumer must NOT swallow it into a silent Pending strand).
/// </summary>
public sealed class WorkflowFinalizedFormSubmissionConsumerTests
{
    private readonly IFormSubmissionStatusClient _statusClient = Substitute.For<IFormSubmissionStatusClient>();
    private readonly ILogger<WorkflowFinalizedFormSubmissionConsumer> _logger =
        Substitute.For<ILogger<WorkflowFinalizedFormSubmissionConsumer>>();
    private readonly WorkflowFinalizedFormSubmissionConsumer _sut;

    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid SubmissionId = Guid.NewGuid();
    private static readonly Guid TransitionedBy = Guid.NewGuid();
    private static readonly Guid MessageId = Guid.NewGuid();

    public WorkflowFinalizedFormSubmissionConsumerTests()
    {
        _sut = new WorkflowFinalizedFormSubmissionConsumer(_statusClient, _logger);
    }

    private static ConsumeContext<WorkflowInstanceFinalizedIntegrationEvent> Context(
        string finalStateName, string entityType = "FormSubmission")
    {
        var ctx = Substitute.For<ConsumeContext<WorkflowInstanceFinalizedIntegrationEvent>>();
        ctx.Message.Returns(new WorkflowInstanceFinalizedIntegrationEvent(
            InstancePublicId: InstanceId,
            EntityType: entityType,
            EntityId: SubmissionId,
            FinalStateName: finalStateName,
            TransitionedBy: TransitionedBy,
            TenantId: Guid.NewGuid(),
            MessageId: MessageId,
            OccurredAt: new DateTimeOffset(2026, 07, 08, 7, 0, 0, TimeSpan.Zero)));
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("ApprovedByManager")]
    [InlineData("approve")]
    public async Task Consume_ApprovedState_ApprovesSubmission(string state)
    {
        await _sut.Consume(Context(state));

        await _statusClient.Received(1).ApproveSubmissionAsync(
            SubmissionId, TransitionedBy, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _statusClient.DidNotReceive().RejectSubmissionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("RejectedByReviewer")]
    [InlineData("reject")]
    public async Task Consume_RejectedState_RejectsSubmission(string state)
    {
        await _sut.Consume(Context(state));

        await _statusClient.Received(1).RejectSubmissionAsync(
            SubmissionId, TransitionedBy, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _statusClient.DidNotReceive().ApproveSubmissionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_UnrecognizedFinalState_AppliesNeither()
    {
        await _sut.Consume(Context("Escalated"));

        await _statusClient.DidNotReceive().ApproveSubmissionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _statusClient.DidNotReceive().RejectSubmissionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_NonFormSubmissionEntity_AppliesNeither()
    {
        await _sut.Consume(Context("Approved", entityType: "BusinessProcessRun"));

        await _statusClient.DidNotReceive().ApproveSubmissionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _statusClient.DidNotReceive().RejectSubmissionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_StatusClientThrowsInfra_Propagates_ForRetry()
    {
        // Durability property: an infra failure must NOT be swallowed — it propagates so MassTransit's
        // UseMessageRetry re-applies the decision instead of leaving the submission silently Pending.
        _statusClient
            .ApproveSubmissionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("form submission DB unavailable"));

        await Assert.ThrowsAsync<TimeoutException>(() => _sut.Consume(Context("Approved")));
    }
}
