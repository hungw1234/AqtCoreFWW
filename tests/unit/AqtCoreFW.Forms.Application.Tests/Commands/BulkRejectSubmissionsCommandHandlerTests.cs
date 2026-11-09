using AqtCoreFW.Forms.Application.Commands.BulkApproveSubmissions;
using AqtCoreFW.Forms.Application.Commands.BulkRejectSubmissions;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for BulkRejectSubmissionsCommandHandler.
/// Verifies event publishing per rejected submission and error handling.
/// Phase-02c: handler now requires IGuidGenerator for Rule1-compliant event MessageId.
/// </summary>
public sealed class BulkRejectSubmissionsCommandHandlerTests
{
    private readonly IFormSubmissionRepository _repository = Substitute.For<IFormSubmissionRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IGuidGenerator _guidGenerator;
    private readonly BulkRejectSubmissionsCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ReviewerId = Guid.NewGuid();

    public BulkRejectSubmissionsCommandHandlerTests()
    {
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.NewIdRandom().Returns(Guid.NewGuid());
        _handler = new BulkRejectSubmissionsCommandHandler(_repository, _guidGenerator, _publisher);
    }

    [Fact]
    public async Task Handle_RejectsAll_PublishesEventPerSubmission()
    {
        var sub1 = CreatePendingSubmission();
        var sub2 = CreatePendingSubmission();
        SetupRepo(sub1, sub2);

        // BulkRejectSubmissionsCommand.Ids = IReadOnlyList<Guid> (PublicIds, T2 AK)
        var command = new BulkRejectSubmissionsCommand([sub1.PublicId, sub2.PublicId], TenantId, ReviewerId, "Rejected reason");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().Be(2);
        result.Value.Failed.Should().Be(0);

        await _publisher.Received(2).Publish(
            Arg.Any<FormRejectedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyRejected_PublishesOnlyForSucceeded()
    {
        var sub1 = CreatePendingSubmission();
        var sub2 = CreatePendingSubmission();
        sub2.Reject(ReviewerId, "pre-rejected"); // already rejected — will throw
        SetupRepo(sub1, sub2);

        var command = new BulkRejectSubmissionsCommand([sub1.PublicId, sub2.PublicId], TenantId, ReviewerId, "Reason");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Succeeded.Should().Be(1);
        result.Value.Failed.Should().Be(1);

        // Event SubmissionId = PublicId (T2 AK)
        await _publisher.Received(1).Publish(
            Arg.Is<FormRejectedEvent>(e => e.SubmissionId == sub1.PublicId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_AddsError_NoEvent()
    {
        var missingPublicId = Guid.NewGuid();
        _repository.GetByIdAsync(missingPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<FormSubmission>("not found"));

        var command = new BulkRejectSubmissionsCommand([missingPublicId], TenantId, ReviewerId, "Reason");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Succeeded.Should().Be(0);
        result.Value.Failed.Should().Be(1);
        await _publisher.DidNotReceive().Publish(
            Arg.Any<FormRejectedEvent>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ──

    private static FormSubmission CreatePendingSubmission()
    {
        // FormSubmission is T2: requires IGuidGenerator for PublicId generation
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdRandom().Returns(Guid.NewGuid());
        return FormSubmission.Create(gen, TenantId, 1L, 1, "{}", Guid.NewGuid());
    }

    private void SetupRepo(params FormSubmission[] subs)
    {
        foreach (var s in subs)
            // IFormSubmissionRepository uses Guid (PublicId) as TId
            _repository.GetByIdAsync(s.PublicId, Arg.Any<CancellationToken>())
                .Returns(Result.Ok(s));
    }
}
