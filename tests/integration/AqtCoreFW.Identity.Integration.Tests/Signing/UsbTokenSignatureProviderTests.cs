using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Integration.Tests.Signing.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Unit-style tests for <see cref="UsbTokenSignatureProvider"/> (no DB / no HTTP / no VGCA MSI required).
///
/// Test strategy:
/// - All CMS work uses <see cref="VgcaGoldenSamples"/> (BouncyCastle-generated self-signed cert + signature).
/// - <see cref="ICertificateChainValidator"/> is mocked via NSubstitute to isolate W4 from W3.
/// - Tests verify: NotSupportedException semantics, CMS happy-path, tampered-payload rejection,
///   tampered-signature rejection, capability flags, and chain-validation delegation.
/// </summary>
public sealed class UsbTokenSignatureProviderTests : IClassFixture<VgcaGoldenSamples>
{
    private readonly VgcaGoldenSamples _samples;
    private readonly ICertificateChainValidator _chainValidator;
    private readonly UsbTokenSignatureProvider _sut;

    public UsbTokenSignatureProviderTests(VgcaGoldenSamples samples)
    {
        _samples = samples;

        // Mock chain validator: returns IsValid=true for all calls (W3 domain — not under test here)
        _chainValidator = Substitute.For<ICertificateChainValidator>();
        _chainValidator
            .ValidateAsync(
                Arg.Any<byte[]>(),
                Arg.Any<IEnumerable<byte[]>?>(),
                Arg.Any<OcspMode>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChainValidationResult(
                IsValid: true,
                FailureReason: null,
                Chain: Array.Empty<CertificateInfo>(),
                OcspChecked: false,
                OcspSucceeded: false));

        _sut = new UsbTokenSignatureProvider(
            _chainValidator,
            NullLogger<UsbTokenSignatureProvider>.Instance);
    }

    // ── 1. SignAsync capability contract ─────────────────────────────────────

    [Fact(DisplayName = "SignAsync throws NotSupportedException — VGCA signs FE-side only")]
    public void SignAsync_Always_ThrowsNotSupportedException()
    {
        // Arrange
        var request = new SignRequest(
            Payload: "test"u8.ToArray(),
            UserId: null,
            Reason: null,
            ExtraParameters: null);

        // Act & Assert — SignAsync throws synchronously (before yielding a Task).
        // Cast to Action to force the synchronous Assert.Throws<T>(Action) overload;
        // the lambda body executes synchronously and the throw is caught by xUnit.
        Action act = () => { _ = _sut.SignAsync(request); };
        var ex = Assert.Throws<NotSupportedException>(act);
        ex.Message.Should().Contain("ClientSideSigning");
    }

    // ── 2. Capabilities ───────────────────────────────────────────────────────

    [Fact(DisplayName = "Capabilities has ClientSideSigning and RequiresLocalAgent flags")]
    public void Capabilities_HasClientSideSigning_AndRequiresLocalAgent()
    {
        _sut.Capabilities.Should().HaveFlag(SignatureProviderCapabilities.ClientSideSigning);
        _sut.Capabilities.Should().HaveFlag(SignatureProviderCapabilities.RequiresLocalAgent);
    }

    [Fact(DisplayName = "Capabilities does NOT have ServerSideSigning flag")]
    public void Capabilities_DoesNotHaveServerSideSigning()
    {
        _sut.Capabilities.Should().NotHaveFlag(SignatureProviderCapabilities.ServerSideSigning);
    }

    [Fact(DisplayName = "ProviderKey is 'usb-vgca'")]
    public void ProviderKey_IsUsbVgca()
    {
        _sut.ProviderKey.Should().Be("usb-vgca");
    }

    // ── 3. VerifyAsync — happy path ───────────────────────────────────────────

    [Fact(DisplayName = "VerifyAsync with valid golden CMS sample returns IsValid=true")]
    public async Task VerifyAsync_ValidGoldenSample_ReturnsIsValidTrue()
    {
        // Arrange — golden signature over golden payload, chain validator mocked IsValid=true
        var request = new VerifyRequest(
            Payload: VgcaGoldenSamples.GoldenPayload,
            Signature: _samples.GoldenSignature,
            OcspMode: OcspMode.Soft);

        // Act
        var result = await _sut.VerifyAsync(request);

        // Assert
        result.IsValid.Should().BeTrue(because: "CMS signature matches payload and chain validator approved");
        result.FailureReason.Should().BeNull();
        result.SigningCertificate.Should().NotBeNull();
        result.ChainResult.Should().NotBeNull();
    }

    [Fact(DisplayName = "VerifyAsync populates SigningCertificate subject from CMS embedded cert")]
    public async Task VerifyAsync_ValidGoldenSample_PopulatesSigningCertificateSubject()
    {
        var request = new VerifyRequest(
            Payload: VgcaGoldenSamples.GoldenPayload,
            Signature: _samples.GoldenSignature,
            OcspMode: OcspMode.Soft);

        var result = await _sut.VerifyAsync(request);

        result.SigningCertificate!.Subject.Should().Contain("VGCA Test Signer W4");
        result.SigningCertificate.Issuer.Should().Contain("VGCA Test Signer W4"); // self-signed
        result.SigningCertificate.RawData.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "VerifyAsync delegates to chain validator with correct cert DER bytes")]
    public async Task VerifyAsync_ValidGoldenSample_DelegatesToChainValidator()
    {
        var request = new VerifyRequest(
            Payload: VgcaGoldenSamples.GoldenPayload,
            Signature: _samples.GoldenSignature,
            OcspMode: OcspMode.Hard);

        await _sut.VerifyAsync(request);

        // Chain validator must have been called exactly once with the signer cert DER
        await _chainValidator.Received(1).ValidateAsync(
            Arg.Is<byte[]>(d => d.Length > 0),
            Arg.Any<IEnumerable<byte[]>?>(),
            OcspMode.Hard,
            Arg.Any<CancellationToken>());
    }

    // ── 4. VerifyAsync — tampered payload ─────────────────────────────────────

    [Fact(DisplayName = "VerifyAsync with tampered payload returns IsValid=false")]
    public async Task VerifyAsync_TamperedPayload_ReturnsIsValidFalse()
    {
        // Arrange — same signature but different payload
        var tamperedPayload = "TAMPERED-payload-does-not-match-signature"u8.ToArray();
        var request = new VerifyRequest(
            Payload: tamperedPayload,
            Signature: _samples.GoldenSignature,
            OcspMode: OcspMode.Soft);

        // Act
        var result = await _sut.VerifyAsync(request);

        // Assert
        result.IsValid.Should().BeFalse(because: "payload was tampered after signing");
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    // ── 5. VerifyAsync — tampered signature ──────────────────────────────────

    [Fact(DisplayName = "VerifyAsync with tampered signature bytes returns IsValid=false")]
    public async Task VerifyAsync_TamperedSignature_ReturnsIsValidFalse()
    {
        // Arrange — produce a structurally valid CMS structure but signed over a DIFFERENT payload.
        // This gives a well-formed CMS that parses successfully but whose signature does not
        // match GoldenPayload, ensuring the cryptographic check (signer.Verify) fails — not the parser.
        var differentPayload = "completely-different-payload-not-golden"u8.ToArray();
        var keyPair = VgcaGoldenSamples.GenerateKeyPair();
        var parser = new Org.BouncyCastle.X509.X509CertificateParser();
        var cert = parser.ReadCertificate(_samples.SignerCertDer); // same cert structure, different key
        // Re-sign differentPayload with a NEW key so the cert matches but signature is wrong for GoldenPayload
        var wrongSignature = VgcaGoldenSamples.SignCms(differentPayload, keyPair, cert);

        var request = new VerifyRequest(
            Payload: VgcaGoldenSamples.GoldenPayload,
            Signature: wrongSignature,
            OcspMode: OcspMode.Soft);

        // Act
        var result = await _sut.VerifyAsync(request);

        // Assert — signer.Verify(cert) must fail: cert key doesn't match the embedded sig
        result.IsValid.Should().BeFalse(because: "signature was created with a different key pair");
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    // ── 6. VerifyAsync — chain validation failure propagation ─────────────────

    [Fact(DisplayName = "VerifyAsync propagates chain failure when chain validator returns IsValid=false")]
    public async Task VerifyAsync_ChainValidatorFails_ReturnsIsValidFalse()
    {
        // Arrange — override mock to return chain failure
        _chainValidator
            .ValidateAsync(
                Arg.Any<byte[]>(),
                Arg.Any<IEnumerable<byte[]>?>(),
                Arg.Any<OcspMode>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChainValidationResult(
                IsValid: false,
                FailureReason: "Root certificate thumbprint not in VN CA trust store",
                Chain: Array.Empty<CertificateInfo>(),
                OcspChecked: false,
                OcspSucceeded: false));

        var request = new VerifyRequest(
            Payload: VgcaGoldenSamples.GoldenPayload,
            Signature: _samples.GoldenSignature,
            OcspMode: OcspMode.Soft);

        // Act
        var result = await _sut.VerifyAsync(request);

        // Assert
        result.IsValid.Should().BeFalse(because: "chain validator rejected the certificate");
        result.FailureReason.Should().Contain("trust store");
    }

    // ── 7. VerifyAsync — guard clauses ────────────────────────────────────────

    [Fact(DisplayName = "VerifyAsync with null/empty payload returns IsValid=false")]
    public async Task VerifyAsync_EmptyPayload_ReturnsIsValidFalse()
    {
        var request = new VerifyRequest(
            Payload: Array.Empty<byte>(),
            Signature: _samples.GoldenSignature,
            OcspMode: OcspMode.Soft);

        var result = await _sut.VerifyAsync(request);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("Payload");
    }

    [Fact(DisplayName = "VerifyAsync with null/empty signature returns IsValid=false")]
    public async Task VerifyAsync_EmptySignature_ReturnsIsValidFalse()
    {
        var request = new VerifyRequest(
            Payload: VgcaGoldenSamples.GoldenPayload,
            Signature: Array.Empty<byte>(),
            OcspMode: OcspMode.Soft);

        var result = await _sut.VerifyAsync(request);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("Signature");
    }

    [Fact(DisplayName = "VerifyAsync with corrupt (non-CMS) signature bytes returns IsValid=false")]
    public async Task VerifyAsync_CorruptSignatureBytes_ReturnsIsValidFalse()
    {
        var garbage = "not-a-valid-cms-structure-at-all"u8.ToArray();
        var request = new VerifyRequest(
            Payload: VgcaGoldenSamples.GoldenPayload,
            Signature: garbage,
            OcspMode: OcspMode.Soft);

        var result = await _sut.VerifyAsync(request);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }
}
