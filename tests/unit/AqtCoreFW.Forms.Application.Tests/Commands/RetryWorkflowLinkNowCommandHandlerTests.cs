using AqtCoreFW.Forms.Application.Commands.RetryWorkflowLinkNow;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for the Phase-05 admin Retry-now handler — asserts the LOCKED Q4 status-branch
/// (Shelved→un-shelve, Pending/Retrying→re-arm, Succeeded→conflict) drives the correct Phase-04
/// entity mutator and persists via the repository.
/// </summary>
public sealed class RetryWorkflowLinkNowCommandHandlerTests
{
    private readonly IFormSubmissionWorkflowLinkPendingAttemptRepository _repository =
        Substitute.For<IFormSubmissionWorkflowLinkPendingAttemptRepository>();
    private readonly RetryWorkflowLinkNowCommandHandler _handler;

    public RetryWorkflowLinkNowCommandHandlerTests()
    {
        _handler = new RetryWorkflowLinkNowCommandHandler(_repository);
    }

    private static FormSubmissionWorkflowLinkPendingAttempt NewPendingRow(Guid tenantId, Guid submissionId) =>
        FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
            tenantId, submissionId, formTemplateId: 1, approvalWorkflowDefinitionId: Guid.NewGuid(),
            submittedBy: Guid.NewGuid(), failureCode: "GOV_WFL_014", failureMessage: "no handler");

    [Fact]
    public async Task Handle_RowNotFound_ReturnsFailure()
    {
        var command = new RetryWorkflowLinkNowCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _repository.GetTrackedBySubmissionIdAsync(command.SubmissionId, command.TenantId, Arg.Any<CancellationToken>())
            .Returns((FormSubmissionWorkflowLinkPendingAttempt?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<FormSubmissionWorkflowLinkPendingAttempt>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PendingRow_ArmsForImmediateRetry_WithoutIncrementingAttemptCount()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var row = NewPendingRow(tenantId, submissionId);
        var command = new RetryWorkflowLinkNowCommand(submissionId, tenantId, Guid.NewGuid());
        _repository.GetTrackedBySubmissionIdAsync(submissionId, tenantId, Arg.Any<CancellationToken>()).Returns(row);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.Status.Should().Be(WorkflowLinkPendingStatus.Pending);
        row.AttemptCount.Should().Be(0, "a manual nudge is not a failed attempt");
        row.NextRetryAt.Should().NotBeNull();
        row.NextRetryAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        await _repository.Received(1).UpdateAsync(row, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RetryingRow_ArmsForImmediateRetry()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var row = NewPendingRow(tenantId, submissionId);
        row.MarkRetrying(Guid.Empty);
        var command = new RetryWorkflowLinkNowCommand(submissionId, tenantId, Guid.NewGuid());
        _repository.GetTrackedBySubmissionIdAsync(submissionId, tenantId, Arg.Any<CancellationToken>()).Returns(row);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.Status.Should().Be(WorkflowLinkPendingStatus.Retrying);
        row.NextRetryAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ShelvedRow_ReactivatesToPending_AndClearsResolvedFields()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var row = NewPendingRow(tenantId, submissionId);
        row.MarkShelved(Guid.NewGuid());
        row.Status.Should().Be(WorkflowLinkPendingStatus.Shelved); // sanity: precondition for this test

        var actorId = Guid.NewGuid();
        var command = new RetryWorkflowLinkNowCommand(submissionId, tenantId, actorId);
        _repository.GetTrackedBySubmissionIdAsync(submissionId, tenantId, Arg.Any<CancellationToken>()).Returns(row);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.Status.Should().Be(WorkflowLinkPendingStatus.Pending, "manual Retry-now un-shelves (LOCKED Q4)");
        row.NextRetryAt.Should().NotBeNull();
        row.ResolvedAt.Should().BeNull();
        row.ResolvedBy.Should().BeNull();
        await _repository.Received(1).UpdateAsync(row, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SucceededRow_ReturnsConflict_AndDoesNotPersist()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var row = NewPendingRow(tenantId, submissionId);
        row.MarkSucceeded(Guid.NewGuid(), Guid.NewGuid());
        var command = new RetryWorkflowLinkNowCommand(submissionId, tenantId, Guid.NewGuid());
        _repository.GetTrackedBySubmissionIdAsync(submissionId, tenantId, Arg.Any<CancellationToken>()).Returns(row);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        row.Status.Should().Be(WorkflowLinkPendingStatus.Succeeded);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<FormSubmissionWorkflowLinkPendingAttempt>(), Arg.Any<CancellationToken>());
    }
}
