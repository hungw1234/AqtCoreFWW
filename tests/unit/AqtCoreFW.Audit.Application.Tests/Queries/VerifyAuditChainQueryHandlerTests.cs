using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Application.Queries.VerifyAuditChain;
using AqtCoreFW.Audit.Application.Services;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Audit.Application.Tests.Queries;

/// <summary>
/// Unit tests for VerifyAuditChainQueryHandler.
/// Handler is a thin delegator — all logic lives in IHmacChainVerifier.
/// Tests verify delegation, result wrapping, and tamper-detected path.
/// </summary>
public sealed class VerifyAuditChainQueryHandlerTests
{
    private readonly IHmacChainVerifier _verifier = Substitute.For<IHmacChainVerifier>();
    private readonly VerifyAuditChainQueryHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    public VerifyAuditChainQueryHandlerTests()
    {
        _sut = new VerifyAuditChainQueryHandler(_verifier);
    }

    // --- Happy path: valid chain ---

    [Fact]
    public async Task Handle_ValidChain_ReturnsSuccessResult()
    {
        var dto = new ChainVerificationResultDto(IsValid: true, EntriesChecked: 500, FirstTamperedEntryId: null, TamperDetail: null);
        _verifier.VerifyAsync(TenantId, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await _sut.Handle(new VerifyAuditChainQuery(TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidChain_DtoIsValid()
    {
        var dto = new ChainVerificationResultDto(IsValid: true, EntriesChecked: 200, FirstTamperedEntryId: null, TamperDetail: null);
        _verifier.VerifyAsync(TenantId, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await _sut.Handle(new VerifyAuditChainQuery(TenantId), CancellationToken.None);

        result.Value.IsValid.Should().BeTrue();
        result.Value.EntriesChecked.Should().Be(200);
        result.Value.FirstTamperedEntryId.Should().BeNull();
    }

    // --- Tamper detected path ---

    [Fact]
    public async Task Handle_TamperedChain_ReturnsSuccessWithInvalidDto()
    {
        var tamperedId = Guid.NewGuid();
        var dto = new ChainVerificationResultDto(
            IsValid: false,
            EntriesChecked: 150,
            FirstTamperedEntryId: tamperedId,
            TamperDetail: "HMAC mismatch at entry 150");
        _verifier.VerifyAsync(TenantId, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await _sut.Handle(new VerifyAuditChainQuery(TenantId), CancellationToken.None);

        // Handler wraps result in Result.Ok — tamper is a domain outcome, not a failure
        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.FirstTamperedEntryId.Should().Be(tamperedId);
        result.Value.TamperDetail.Should().Contain("HMAC mismatch");
    }

    // --- Delegation ---

    [Fact]
    public async Task Handle_Always_DelegatesVerifyToVerifierWithCorrectTenantId()
    {
        _verifier.VerifyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ChainVerificationResultDto(true, 0, null, null));

        await _sut.Handle(new VerifyAuditChainQuery(TenantId), CancellationToken.None);

        await _verifier.Received(1).VerifyAsync(TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VerifierThrows_ExceptionPropagates()
    {
        _verifier.VerifyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<ChainVerificationResultDto>(_ => throw new InvalidOperationException("Verifier unavailable"));

        var act = async () => await _sut.Handle(new VerifyAuditChainQuery(TenantId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Verifier unavailable*");
    }
}
