using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Strategies;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Execution-surface coverage for <see cref="PadesAssemblerSigningStrategy"/>. The strategy is a thin delegator:
/// it validates the PAdES parameters are present (a 400, not a 500, when missing) and otherwise hands off to
/// <see cref="IPadesTransactionInitiator"/>. Routing (<c>CanHandle</c>) is covered by SigningStrategyRouterTests.
/// </summary>
public sealed class PadesAssemblerSigningStrategyTests
{
    private static SigningExecutionContext Context(PadesSignParameters? pades) =>
        new(
            new RequestSignatureInput(
                TenantId: Guid.NewGuid(),
                ArtifactKind: SigningArtifactKind.UploadedPdf,
                ArtifactRef: Guid.NewGuid().ToString(),
                ProviderKey: "remote-viettel-mysign",
                AssignedSignerId: Guid.NewGuid(),
                InitiatedBy: Guid.NewGuid(),
                Pades: pades),
            Substitute.For<IDigitalSignatureProvider>());

    [Fact]
    public async Task ExecuteAsync_NoPadesParams_ReturnsBadRequest_WithoutDelegating()
    {
        var initiator = Substitute.For<IPadesTransactionInitiator>();
        var strategy = new PadesAssemblerSigningStrategy(initiator);

        var result = await strategy.ExecuteAsync(Context(pades: null));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<BadRequestError>();
        await initiator.DidNotReceive().InitiateAsync(Arg.Any<SigningExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithPadesParams_DelegatesToInitiator()
    {
        var expected = Result.Ok(new SigningExecutionResult(
            SigningUnderlyingKind.PendingTransaction, "txn-9",
            new Dictionary<string, string> { ["pollable"] = "true" }));
        var initiator = Substitute.For<IPadesTransactionInitiator>();
        initiator.InitiateAsync(Arg.Any<SigningExecutionContext>(), Arg.Any<CancellationToken>()).Returns(expected);
        var strategy = new PadesAssemblerSigningStrategy(initiator);

        var result = await strategy.ExecuteAsync(Context(new PadesSignParameters("tenant/orig.pdf")));

        result.IsSuccess.Should().BeTrue();
        result.Value.UnderlyingRecordId.Should().Be("txn-9");
        await initiator.Received(1).InitiateAsync(Arg.Any<SigningExecutionContext>(), Arg.Any<CancellationToken>());
    }
}
