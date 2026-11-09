using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Strategies;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using FluentResults;
using MediatR;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Execution coverage for <see cref="DocFlowSigningStrategy"/>. The strategy delegates to the shipped
/// <see cref="InitiateDocFlowCommand"/> (so its tenant-prefix guard + audit fire) and maps the result into the
/// generic <see cref="SigningExecutionResult"/>: underlying-record link + client hand-off. Expected client and
/// validation failures come back as a failed <see cref="Result{T}"/> (→ 400/422), not a thrown 500; only a
/// success-with-no-record-id invariant violation throws.
/// </summary>
public sealed class DocFlowSigningStrategyTests
{
    private static readonly Guid DocId = Guid.NewGuid();

    private static SigningExecutionContext ContextFor(DocFlowSignParameters? docFlow, string? artifactRef = null)
    {
        var input = new RequestSignatureInput(
            TenantId:         Guid.NewGuid(),
            ArtifactKind:     SigningArtifactKind.UploadedPdf,
            ArtifactRef:      artifactRef ?? DocId.ToString(),
            ProviderKey:      "usb-vgca",
            AssignedSignerId: Guid.NewGuid(),
            InitiatedBy:      Guid.NewGuid(),
            DocFlow:          docFlow);
        return new SigningExecutionContext(input, Substitute.For<IDigitalSignatureProvider>());
    }

    private static DocFlowSignParameters ValidParams() => new(
        SignType:           DocFlowSignType.Issued,
        OriginalStorageKey: "tenant/original.pdf",
        DocNumber:          "VB-001",
        IssuedDate:         new DateTime(2026, 6, 16),
        BaseUrl:            "https://api.example.com");

    [Fact]
    public async Task ExecuteAsync_OnSuccess_LinksRecord_AndReturnsHandoff()
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<InitiateDocFlowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new InitiateDocFlowResultDto(
                HandlerUrl: "https://api.example.com/api/signing/docflow/file",
                Jwt:        "the-jwt",
                DocId:      DocId,
                RecordId:   4242L)));

        var result = await new DocFlowSigningStrategy(mediator).ExecuteAsync(ContextFor(ValidParams()));

        result.IsSuccess.Should().BeTrue();
        result.Value.UnderlyingKind.Should().Be(SigningUnderlyingKind.DocFlow);
        result.Value.UnderlyingRecordId.Should().Be("4242");
        result.Value.ClientHandoff["handlerUrl"].Should().Be("https://api.example.com/api/signing/docflow/file");
        result.Value.ClientHandoff["jwt"].Should().Be("the-jwt");
        result.Value.ClientHandoff["docId"].Should().Be(DocId.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_FailsBadRequest_WhenDocFlowParamsMissing()
    {
        var result = await new DocFlowSigningStrategy(Substitute.For<ISender>())
            .ExecuteAsync(ContextFor(docFlow: null));

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainItemsAssignableTo<BadRequestError>();
    }

    [Fact]
    public async Task ExecuteAsync_FailsBadRequest_WhenArtifactRefIsNotAGuid()
    {
        var result = await new DocFlowSigningStrategy(Substitute.For<ISender>())
            .ExecuteAsync(ContextFor(ValidParams(), artifactRef: "not-a-guid"));

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainItemsAssignableTo<BadRequestError>();
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesInnerError_WhenInitiationFails()
    {
        // A failed inner command (e.g. the tenant-prefix guard rejected the storage key) must propagate with its
        // error TYPE intact — not collapse into a generic 500 — so the API maps the right 4xx status.
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<InitiateDocFlowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<InitiateDocFlowResultDto>(
                new ValidationError("storage key outside tenant prefix", "OriginalStorageKey")));

        var result = await new DocFlowSigningStrategy(mediator).ExecuteAsync(ContextFor(ValidParams()));

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainItemsAssignableTo<ValidationError>();
    }

    [Fact]
    public async Task ExecuteAsync_Throws_WhenSuccessButNoRecordId()
    {
        // Success with no record id is an internal invariant violation (500), not a client error.
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<InitiateDocFlowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new InitiateDocFlowResultDto("url", "jwt", DocId, RecordId: null)));

        var act = async () => await new DocFlowSigningStrategy(mediator).ExecuteAsync(ContextFor(ValidParams()));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
