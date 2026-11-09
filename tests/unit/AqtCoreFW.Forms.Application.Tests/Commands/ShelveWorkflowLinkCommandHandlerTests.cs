using AqtCoreFW.Forms.Application.Commands.ShelveWorkflowLink;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>Unit tests for the Phase-05 admin Shelve (manual dead-letter) handler.</summary>
public sealed class ShelveWorkflowLinkCommandHandlerTests
{
    private readonly IFormSubmissionWorkflowLinkPendingAttemptRepository _repository =
        Substitute.For<IFormSubmissionWorkflowLinkPendingAttemptRepository>();
    private readonly ShelveWorkflowLinkCommandHandler _handler;

    public ShelveWorkflowLinkCommandHandlerTests()
    {
        _handler = new ShelveWorkflowLinkCommandHandler(_repository);
    }

    private static FormSubmissionWorkflowLinkPendingAttempt NewPendingRow(Guid tenantId, Guid submissionId) =>
        FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
            tenantId, submissionId, formTemplateId: 1, approvalWorkflowDefinitionId: Guid.NewGuid(),
            submittedBy: Guid.NewGuid(), failureCode: "GOV_WFL_014", failureMessage: "no handler");

    [Fact]
    public async Task Handle_RowNotFound_ReturnsFailure()
    {
        var command = new ShelveWorkflowLinkCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _repository.GetTrackedBySubmissionIdAsync(command.SubmissionId, command.TenantId, Arg.Any<CancellationToken>())
            .Returns((FormSubmissionWorkflowLinkPendingAttempt?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_PendingRow_MarksShelved_WithResolvedByActor()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var row = NewPendingRow(tenantId, submissionId);
        var command = new ShelveWorkflowLinkCommand(submissionId, tenantId, actorId);
        _repository.GetTrackedBySubmissionIdAsync(submissionId, tenantId, Arg.Any<CancellationToken>()).Returns(row);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.Status.Should().Be(WorkflowLinkPendingStatus.Shelved);
        row.ResolvedBy.Should().Be(actorId);
        row.ResolvedAt.Should().NotBeNull();
        await _repository.Received(1).UpdateAsync(row, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadySucceededRow_ReturnsConflict_AndDoesNotPersist()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var row = NewPendingRow(tenantId, submissionId);
        row.MarkSucceeded(Guid.NewGuid(), Guid.NewGuid());
        var command = new ShelveWorkflowLinkCommand(submissionId, tenantId, Guid.NewGuid());
        _repository.GetTrackedBySubmissionIdAsync(submissionId, tenantId, Arg.Any<CancellationToken>()).Returns(row);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<FormSubmissionWorkflowLinkPendingAttempt>(), Arg.Any<CancellationToken>());
    }
}
