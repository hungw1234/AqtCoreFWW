using AqtCoreFW.Forms.Application.Commands.RejectSubmission;
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
/// Unit tests for RejectSubmissionCommandHandler.
/// Covers: happy path, already-rejected guard, workflow-managed warning log, empty comment.
/// Phase-02c: handler now requires IGuidGenerator for Rule1-compliant event MessageId.
/// </summary>
public sealed class RejectSubmissionCommandHandlerTests
{
    private readonly IFormSubmissionRepository _repository = Substitute.For<IFormSubmissionRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly ILogger<RejectSubmissionCommandHandler> _logger = Substitute.For<ILogger<RejectSubmissionCommandHandler>>();
    private readonly IGuidGenerator _guidGenerator;
    private readonly RejectSubmissionCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ReviewerId = Guid.NewGuid();

    public RejectSubmissionCommandHandlerTests()
    {
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.NewIdRandom().Returns(Guid.NewGuid());
        _handler = new RejectSubmissionCommandHandler(_repository, _guidGenerator, _publisher, _logger);
    }

    [Fact]
    public async Task Handle_PendingSubmission_RejectsAndPublishesEvent()
    {
        var sub = CreateSubmission();
        SetupRepo(sub);

        // RejectSubmissionCommand.SubmissionId = Guid (PublicId, T2 AK)
        var command = new RejectSubmissionCommand(sub.PublicId, TenantId, ReviewerId, "Not compliant");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubmissionStatus.Rejected);
        await _publisher.Received(1).Publish(
            Arg.Is<FormRejectedEvent>(e => e.SubmissionId == sub.PublicId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyRejected_ReturnsFailure_NoEvent()
    {
        var sub = CreateSubmission();
        sub.Reject(ReviewerId, "first rejection");
        SetupRepo(sub);

        var command = new RejectSubmissionCommand(sub.PublicId, TenantId, ReviewerId, "second attempt");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _publisher.DidNotReceive().Publish(
            Arg.Any<FormRejectedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyComment_ReturnsFailure()
    {
        var sub = CreateSubmission();
        SetupRepo(sub);

        var command = new RejectSubmissionCommand(sub.PublicId, TenantId, ReviewerId, "");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _publisher.DidNotReceive().Publish(
            Arg.Any<FormRejectedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WorkflowManaged_LogsWarning_StillRejects()
    {
        var sub = CreateSubmission();
        sub.LinkWorkflow(Guid.NewGuid());
        SetupRepo(sub);

        var command = new RejectSubmissionCommand(sub.PublicId, TenantId, ReviewerId, "Manual override rejection");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubmissionStatus.Rejected);

        // Verify warning was logged for workflow-managed submission
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("workflow-managed")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
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
