using AqtCoreFW.Forms.Application.Commands.ApproveSubmission;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for ApproveSubmissionCommandHandler.
/// Covers: happy path, already-approved guard, workflow-managed warning log.
/// Phase-02c: handler now requires IGuidGenerator for Rule1-compliant event MessageId.
/// </summary>
public sealed class ApproveSubmissionCommandHandlerTests
{
    private readonly IFormSubmissionRepository _repository = Substitute.For<IFormSubmissionRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly ILogger<ApproveSubmissionCommandHandler> _logger = Substitute.For<ILogger<ApproveSubmissionCommandHandler>>();
    private readonly IGuidGenerator _guidGenerator;
    private readonly ApproveSubmissionCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ReviewerId = Guid.NewGuid();

    public ApproveSubmissionCommandHandlerTests()
    {
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.NewIdRandom().Returns(Guid.NewGuid());
        _handler = new ApproveSubmissionCommandHandler(_repository, _guidGenerator, _publisher, _logger);
    }

    [Fact]
    public async Task Handle_PendingSubmission_ApprovesAndPublishesEvent()
    {
        var sub = CreateSubmission();
        SetupRepo(sub);

        // ApproveSubmissionCommand.SubmissionId = Guid (PublicId, T2 AK)
        var command = new ApproveSubmissionCommand(sub.PublicId, TenantId, ReviewerId, "Approved");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubmissionStatus.Approved);
        await _publisher.Received(1).Publish(
            Arg.Is<FormApprovedEvent>(e => e.SubmissionId == sub.PublicId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyApproved_ReturnsFailure_NoEvent()
    {
        var sub = CreateSubmission();
        sub.Approve(ReviewerId, "first approval");
        SetupRepo(sub);

        var command = new ApproveSubmissionCommand(sub.PublicId, TenantId, ReviewerId, "second attempt");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _publisher.DidNotReceive().Publish(
            Arg.Any<FormApprovedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WorkflowManaged_LogsWarning_StillApproves()
    {
        var sub = CreateSubmission();
        sub.LinkWorkflow(Guid.NewGuid());
        SetupRepo(sub);

        var command = new ApproveSubmissionCommand(sub.PublicId, TenantId, ReviewerId, "Manual override");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubmissionStatus.Approved);

        // Verify warning was logged for workflow-managed submission
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("workflow-managed")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailure()
    {
        var missingPublicId = Guid.NewGuid();
        _repository.GetByIdAsync(missingPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<FormSubmission>("not found"));

        var command = new ApproveSubmissionCommand(missingPublicId, TenantId, ReviewerId, "OK");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WrongTenant_ReturnsFailure()
    {
        var sub = CreateSubmission();
        SetupRepo(sub);

        var wrongTenant = Guid.NewGuid();
        var command = new ApproveSubmissionCommand(sub.PublicId, wrongTenant, ReviewerId, "OK");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // ── Helpers ──

    private static FormSubmission CreateSubmission()
    {
        // FormSubmission is T2: requires IGuidGenerator for PublicId generation
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdRandom().Returns(Guid.NewGuid());
        return FormSubmission.Create(gen, TenantId, 1L, 1, "{}", Guid.NewGuid());
    }

    private void SetupRepo(FormSubmission sub) =>
        // IFormSubmissionRepository uses Guid (PublicId) as TId
        _repository.GetByIdAsync(sub.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(sub));
}
