using AqtCoreFW.Forms.Application.Commands.BulkApproveSubmissions;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for BulkApproveSubmissionsCommandHandler.
/// Verifies event publishing per approved submission and error handling.
/// Phase-02c: handler now requires IGuidGenerator for Rule1-compliant event MessageId.
/// </summary>
public sealed class BulkApproveSubmissionsCommandHandlerTests
{
    private readonly IFormSubmissionRepository _repository = Substitute.For<IFormSubmissionRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IGuidGenerator _guidGenerator;
    private readonly BulkApproveSubmissionsCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ReviewerId = Guid.NewGuid();

    public BulkApproveSubmissionsCommandHandlerTests()
    {
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.NewIdRandom().Returns(Guid.NewGuid());
        _handler = new BulkApproveSubmissionsCommandHandler(_repository, _guidGenerator, _publisher);
    }

    [Fact]
    public async Task Handle_ApprovesAll_PublishesEventPerSubmission()
    {
        var sub1 = CreateSubmission();
        var sub2 = CreateSubmission();
        SetupRepo(sub1, sub2);

        // BulkApproveSubmissionsCommand.Ids = IReadOnlyList<Guid> (PublicIds, T2 AK)
        var command = new BulkApproveSubmissionsCommand([sub1.PublicId, sub2.PublicId], TenantId, ReviewerId, "OK");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().Be(2);
        result.Value.Failed.Should().Be(0);

        await _publisher.Received(2).Publish(
            Arg.Any<FormApprovedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PartialFailure_PublishesOnlyForSucceeded()
    {
        var sub1 = CreateSubmission();
        var sub2 = CreateSubmission(SubmissionStatus.Approved); // already approved — will throw
        SetupRepo(sub1, sub2);

        var command = new BulkApproveSubmissionsCommand([sub1.PublicId, sub2.PublicId], TenantId, ReviewerId, "OK");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().Be(1);
        result.Value.Failed.Should().Be(1);

        // Only 1 event published (for sub1); event SubmissionId = PublicId (T2 AK)
        await _publisher.Received(1).Publish(
            Arg.Is<FormApprovedEvent>(e => e.SubmissionId == sub1.PublicId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_AddsError_NoEvent()
    {
        var missingPublicId = Guid.NewGuid();
        _repository.GetByIdAsync(missingPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<FormSubmission>("not found"));

        var command = new BulkApproveSubmissionsCommand([missingPublicId], TenantId, ReviewerId, "OK");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Succeeded.Should().Be(0);
        result.Value.Failed.Should().Be(1);
        await _publisher.DidNotReceive().Publish(
            Arg.Any<FormApprovedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongTenant_AddsError_NoEvent()
    {
        var sub = CreateSubmission();
        // Repo lookup by PublicId (T2 AK)
        _repository.GetByIdAsync(sub.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(sub));

        var wrongTenant = Guid.NewGuid();
        var command = new BulkApproveSubmissionsCommand([sub.PublicId], wrongTenant, ReviewerId, "OK");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Succeeded.Should().Be(0);
        result.Value.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e => e.Contains("access denied"));
    }

    // ── Helpers ──

    private static FormSubmission CreateSubmission(SubmissionStatus status = SubmissionStatus.Pending)
    {
        // FormSubmission is T2: requires IGuidGenerator for PublicId generation
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdRandom().Returns(Guid.NewGuid());
        var sub = FormSubmission.Create(gen, TenantId, 1L, 1, "{}", Guid.NewGuid());
        if (status == SubmissionStatus.Approved)
            sub.Approve(ReviewerId, "pre-approved");
        return sub;
    }

    private void SetupRepo(params FormSubmission[] subs)
    {
        foreach (var s in subs)
            // IFormSubmissionRepository uses Guid (PublicId) as TId
            _repository.GetByIdAsync(s.PublicId, Arg.Any<CancellationToken>())
                .Returns(Result.Ok(s));
    }
}
