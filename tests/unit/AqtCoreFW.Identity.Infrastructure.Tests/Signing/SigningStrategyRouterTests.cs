using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Strategies;
using FluentAssertions;
using MediatR;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Routing-matrix coverage for <see cref="SigningStrategyRouter"/> using the three real strategies. Asserts each
/// of the 6 valid <c>(artifact kind × provider)</c> cells resolves to exactly the right strategy, that an
/// unsupported cell throws <see cref="NotSupportedException"/> (never silent), and that an over-broad
/// registration is caught as ambiguous.
/// </summary>
public sealed class SigningStrategyRouterTests
{
    // USB token (VGCA): client-side raw signer + doc-flow.
    private const SignatureProviderCapabilities UsbCaps =
        SignatureProviderCapabilities.ClientSideSigning
        | SignatureProviderCapabilities.RequiresLocalAgent
        | SignatureProviderCapabilities.RawSign
        | SignatureProviderCapabilities.DocFlow;

    // Viettel Mysign: server-side hash signer only.
    private const SignatureProviderCapabilities RemoteCaps =
        SignatureProviderCapabilities.ServerSideSigning
        | SignatureProviderCapabilities.RawSign;

    private static readonly IReadOnlyList<ISigningStrategy> RealStrategies =
    [
        new DocFlowSigningStrategy(Substitute.For<ISender>()), // routing tests exercise CanHandle only
        new DetachedDataSigningStrategy(),
        new PadesAssemblerSigningStrategy(Substitute.For<IPadesTransactionInitiator>()), // routing tests exercise CanHandle only
    ];

    private static SigningStrategyRouter RouterFor(string key, SignatureProviderCapabilities caps)
    {
        var resolver = Substitute.For<ISignatureProviderResolver>();
        resolver.Resolve(key).Returns(new FakeProvider(key, caps));
        return new SigningStrategyRouter(resolver, RealStrategies);
    }

    [Theory]
    [InlineData(SigningArtifactKind.DetachedData, "usb-vgca",             "detached")]
    [InlineData(SigningArtifactKind.DetachedData, "remote-viettel-mysign", "detached")]
    [InlineData(SigningArtifactKind.FormPdf,      "usb-vgca",             "docflow")]
    [InlineData(SigningArtifactKind.FormPdf,      "remote-viettel-mysign", "pades")]
    [InlineData(SigningArtifactKind.UploadedPdf,  "usb-vgca",             "docflow")]
    [InlineData(SigningArtifactKind.UploadedPdf,  "remote-viettel-mysign", "pades")]
    public void Route_MapsEveryMatrixCell(SigningArtifactKind kind, string key, string expectedStrategy)
    {
        var caps = key == "usb-vgca" ? UsbCaps : RemoteCaps;
        var route = RouterFor(key, caps).Route(kind, key);

        route.Strategy.Name.Should().Be(expectedStrategy);
        route.Provider.ProviderKey.Should().Be(key);
    }

    [Fact]
    public void Route_PadesNeverMatchesUsb_DespiteSharedRawSign()
    {
        // C5: USB declares RawSign + ClientSideSigning; the PAdES strategy must exclude ClientSideSigning so a
        // PDF artifact on the USB provider routes to doc-flow, never to the server-side assembler.
        var route = RouterFor("usb-vgca", UsbCaps).Route(SigningArtifactKind.FormPdf, "usb-vgca");
        route.Strategy.Name.Should().Be("docflow");
    }

    [Fact]
    public void Route_Throws_WhenNoStrategyMatches()
    {
        // Provider with RawSign only: a PDF artifact matches neither doc-flow (needs DocFlow+ClientSide) nor
        // PAdES (needs ServerSideSigning).
        var router = RouterFor("odd", SignatureProviderCapabilities.RawSign);
        var act = () => router.Route(SigningArtifactKind.FormPdf, "odd");
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Route_Throws_WhenRegistrationIsAmbiguous()
    {
        var resolver = Substitute.For<ISignatureProviderResolver>();
        resolver.Resolve("dup").Returns(new FakeProvider("dup", RemoteCaps));
        var ambiguous = new ISigningStrategy[] { new AlwaysHandles("a"), new AlwaysHandles("b") };

        var router = new SigningStrategyRouter(resolver, ambiguous);
        var act = () => router.Route(SigningArtifactKind.FormPdf, "dup");
        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class AlwaysHandles(string name) : ISigningStrategy
    {
        public string Name => name;
        public bool CanHandle(SigningArtifactKind artifactKind, SignatureProviderCapabilities resolvedCapabilities)
            => true;
    }

    private sealed class FakeProvider(string key, SignatureProviderCapabilities caps) : IDigitalSignatureProvider
    {
        public string ProviderKey => key;
        public string DisplayName => key;
        public SignatureProviderCapabilities Capabilities => caps;

        public Task<SignResult> SignAsync(SignRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("router test stub — not invoked");

        public Task<VerifyResult> VerifyAsync(VerifyRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("router test stub — not invoked");
    }
}
