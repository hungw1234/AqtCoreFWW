using AqtCoreFW.Identity.Application.Commands.RevokeToken;
using AqtCoreFW.Identity.Application.Commands.WithdrawConsent;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using MediatR;
using NSubstitute;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for WithdrawConsentCommandHandler.
/// Covers: success path, ownership violation, not-found, already-withdrawn,
/// and that RevokeToken is sent after successful withdrawal.
/// </summary>
public sealed class WithdrawConsentCommandHandlerTests
{
    private readonly IConsentRepository _consents;
    private readonly ISender _sender;
    private readonly WithdrawConsentCommandHandler _sut;

    private static readonly Guid ConsentId = Guid.NewGuid();
    private static readonly Guid DataSubjectId = Guid.NewGuid();

    public WithdrawConsentCommandHandlerTests()
    {
        _consents = Substitute.For<IConsentRepository>();
        _sender = Substitute.For<ISender>();
        _sut = new WithdrawConsentCommandHandler(_consents, _sender);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidOwnerWithdraws_ReturnsSuccess()
    {
        var record = BuildConsent();
        _consents.GetByPublicIdAsync(ConsentId, Arg.Any<CancellationToken>()).Returns(record);

        var result = await _sut.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidOwnerWithdraws_SetsWithdrawnFlagAndTimestamp()
    {
        var record = BuildConsent();
        _consents.GetByPublicIdAsync(ConsentId, Arg.Any<CancellationToken>()).Returns(record);

        await _sut.Handle(BuildCommand(), CancellationToken.None);

        record.IsWithdrawn.Should().BeTrue();
        record.WithdrawnAt.Should().NotBeNull()
            .And.BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_ValidOwnerWithdraws_SavesChanges()
    {
        _consents.GetByPublicIdAsync(ConsentId, Arg.Any<CancellationToken>()).Returns(BuildConsent());

        await _sut.Handle(BuildCommand(), CancellationToken.None);

        await _consents.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidOwnerWithdraws_SendsRevokeTokenForDataSubject()
    {
        _consents.GetByPublicIdAsync(ConsentId, Arg.Any<CancellationToken>()).Returns(BuildConsent());

        await _sut.Handle(BuildCommand(), CancellationToken.None);

        // Handler sends generic Send<Result> — check RevokeTokenCommand with DataSubjectId
        await _sender.Received(1).Send(
            Arg.Is<RevokeTokenCommand>(c => c.UserId == DataSubjectId),
            Arg.Any<CancellationToken>());
    }

    // --- Not found ---

    [Fact]
    public async Task Handle_ConsentNotFound_ReturnsFailure()
    {
        _consents.GetByPublicIdAsync(ConsentId, Arg.Any<CancellationToken>()).Returns((ConsentRecord?)null);

        var result = await _sut.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(ConsentId.ToString()));
    }

    [Fact]
    public async Task Handle_ConsentNotFound_DoesNotSendRevokeToken()
    {
        _consents.GetByPublicIdAsync(ConsentId, Arg.Any<CancellationToken>()).Returns((ConsentRecord?)null);

        await _sut.Handle(BuildCommand(), CancellationToken.None);

        await _sender.DidNotReceiveWithAnyArgs().Send(default!, default);
    }

    // --- Ownership violation ---

    [Fact]
    public async Task Handle_DifferentUser_ReturnsForbidenFailure()
    {
        var record = BuildConsent(); // DataSubjectId != cmd.UserId below
        _consents.GetByPublicIdAsync(ConsentId, Arg.Any<CancellationToken>()).Returns(record);

        var cmd = new WithdrawConsentCommand(ConsentId, UserId: Guid.NewGuid(), Reason: "other");
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("own"));
    }

    // --- Already withdrawn ---

    [Fact]
    public async Task Handle_AlreadyWithdrawn_ReturnsConflictFailure()
    {
        var record = BuildConsent(isWithdrawn: true);
        _consents.GetByPublicIdAsync(ConsentId, Arg.Any<CancellationToken>()).Returns(record);

        var result = await _sut.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("already") || e.Message.Contains("withdrawn"));
    }

    // --- Helpers ---

    // Simple IGuidGenerator stub that always returns a fixed Guid — avoids NSubstitute contamination.
    private sealed class FixedGuidGenerator(Guid id) : IGuidGenerator
    {
        public Guid NewIdMonotonic() => id;
        public Guid NewIdRandom() => id;
    }

    private static ConsentRecord BuildConsent(bool isWithdrawn = false)
    {
        // Use factory with a fixed-Guid generator so PublicId == ConsentId
        var record = ConsentRecord.Create(
            new FixedGuidGenerator(ConsentId),
            dataSubjectId: DataSubjectId,
            purpose: "case_management",
            legalBasis: "Consent",
            dataSubjectType: "citizen",
            evidenceJson: null,
            expiresAt: null,
            tenantId: null,
            actorId: DataSubjectId);

        if (isWithdrawn)
        {
            // Withdraw via domain method to set IsWithdrawn=true, WithdrawnAt=now
            record.Withdraw("pre-withdrawn", DataSubjectId);
        }

        return record;
    }

    private static WithdrawConsentCommand BuildCommand() =>
        new(ConsentId, UserId: DataSubjectId, Reason: "no longer needed");
}
