using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// W1 integration tests for <see cref="UsbTokenSignatureProvider"/> raw RSA/ECDSA verify path.
///
/// <para>Test strategy:</para>
/// <list type="bullet">
///   <item>Generate self-signed RSA-2048 cert via .NET native <see cref="RSA"/> + <see cref="CertificateRequest"/> —
///     avoids BouncyCastle dependency for net10 raw-verify path tests.</item>
///   <item><see cref="ICertificateChainValidator"/> is mocked to return IsValid=true (isolates sig-verify
///     from OCSP/VnCaRootStore chain logic).</item>
///   <item>Four cases: happy-path, tamper-payload, tamper-sig, wrong-cert.</item>
/// </list>
/// </summary>
public sealed class UsbTokenRawVerifyTests : IDisposable
{
    private readonly RSA _rsa;
    private readonly X509Certificate2 _signerCert;
    private readonly ICertificateChainValidator _chainValidator;
    private readonly UsbTokenSignatureProvider _sut;

    private static readonly byte[] KnownPayload = "raw-verify-test-payload-W1-2026"u8.ToArray();

    public UsbTokenRawVerifyTests()
    {
        // Generate RSA-2048 key + self-signed cert (no BouncyCastle needed for native path)
        _rsa = RSA.Create(2048);

        var req = new CertificateRequest(
            "CN=VGCA Test Signer W1, O=AqtCoreFW Test CA, C=VN",
            _rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        _signerCert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // Mock chain validator: returns IsValid=true for all raw verify tests
        // (chain logic = W3 domain, not under test here)
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

    public void Dispose()
    {
        _signerCert.Dispose();
        _rsa.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Signs payload with RSA-PKCS#1-v1.5 SHA-256 over pre-computed hash.</summary>
    private byte[] SignRaw(byte[] payload, RSA key)
    {
        var hash = SHA256.HashData(payload);
        return key.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private VerifyRequest BuildRequest(
        byte[] payload,
        byte[] signature,
        byte[] certDer,
        SignatureFormat format = SignatureFormat.Raw)
        => new(
            Payload: payload,
            Signature: signature,
            OcspMode: OcspMode.Soft,
            SignerCertDer: certDer,
            HashAlg: "SHA256",
            Encode: "RSA",
            Format: format);

    // ── 1. Happy path ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "Raw verify: valid RSA signature returns IsValid=true")]
    public async Task VerifyRaw_ValidSignature_ReturnsIsValidTrue()
    {
        // Arrange
        var sig = SignRaw(KnownPayload, _rsa);
        var request = BuildRequest(KnownPayload, sig, _signerCert.RawData);

        // Act
        var result = await _sut.VerifyAsync(request);

        // Assert
        result.IsValid.Should().BeTrue(because: "signature was created with the correct key over the correct payload");
        result.FailureReason.Should().BeNull();
        result.SigningCertificate.Should().NotBeNull();
        result.SigningCertificate!.Subject.Should().Contain("VGCA Test Signer W1");
        result.ChainResult.Should().NotBeNull();
    }

    // ── 2. Tampered payload ───────────────────────────────────────────────────

    [Fact(DisplayName = "Raw verify: tampered payload returns IsValid=false")]
    public async Task VerifyRaw_TamperedPayload_ReturnsIsValidFalse()
    {
        // Arrange — sign original payload, but verify with 1-byte-flipped payload
        var sig = SignRaw(KnownPayload, _rsa);
        var tamperedPayload = KnownPayload.ToArray();
        tamperedPayload[0] ^= 0xFF; // flip 1 byte

        var request = BuildRequest(tamperedPayload, sig, _signerCert.RawData);

        // Act
        var result = await _sut.VerifyAsync(request);

        // Assert
        result.IsValid.Should().BeFalse(because: "payload was modified after signing");
        result.FailureReason.Should().NotBeNullOrEmpty();
        result.FailureReason.Should().Contain("tampered");
    }

    // ── 3. Tampered signature ─────────────────────────────────────────────────

    [Fact(DisplayName = "Raw verify: tampered signature bytes returns IsValid=false")]
    public async Task VerifyRaw_TamperedSignature_ReturnsIsValidFalse()
    {
        // Arrange — flip 1 byte in the RSA signature
        var sig = SignRaw(KnownPayload, _rsa);
        var tamperedSig = sig.ToArray();
        tamperedSig[sig.Length / 2] ^= 0x01; // flip 1 bit mid-signature

        var request = BuildRequest(KnownPayload, tamperedSig, _signerCert.RawData);

        // Act
        var result = await _sut.VerifyAsync(request);

        // Assert
        result.IsValid.Should().BeFalse(because: "signature bytes were modified");
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    // ── 4. Wrong certificate ──────────────────────────────────────────────────

    [Fact(DisplayName = "Raw verify: signature verified against wrong cert returns IsValid=false")]
    public async Task VerifyRaw_WrongCert_ReturnsIsValidFalse()
    {
        // Arrange — sign with certA key, verify against certB key
        var sig = SignRaw(KnownPayload, _rsa);

        // Generate a second, distinct key pair and self-signed cert
        using var rsaB = RSA.Create(2048);
        var reqB = new CertificateRequest(
            "CN=VGCA Test Signer W1 B, O=AqtCoreFW Test CA, C=VN",
            rsaB,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        reqB.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        using var certB = reqB.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // Verify with certB's DER — public key won't match the signature from certA
        var request = BuildRequest(KnownPayload, sig, certB.RawData);

        // Act
        var result = await _sut.VerifyAsync(request);

        // Assert
        result.IsValid.Should().BeFalse(because: "signature was created with certA but verified against certB");
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    // ── 5. Capabilities includes RawSign flag ─────────────────────────────────

    [Fact(DisplayName = "Capabilities has RawSign flag (W1)")]
    public void Capabilities_HasRawSign()
    {
        _sut.Capabilities.Should().HaveFlag(SignatureProviderCapabilities.RawSign);
    }

    // ── 6. Missing SignerCertDer guard ────────────────────────────────────────

    [Fact(DisplayName = "Raw verify: missing SignerCertDer returns IsValid=false with descriptive reason")]
    public async Task VerifyRaw_MissingSignerCertDer_ReturnsIsValidFalse()
    {
        var sig = SignRaw(KnownPayload, _rsa);
        var request = new VerifyRequest(
            Payload: KnownPayload,
            Signature: sig,
            OcspMode: OcspMode.Soft,
            SignerCertDer: null,
            HashAlg: "SHA256",
            Encode: "RSA",
            Format: SignatureFormat.Raw);

        var result = await _sut.VerifyAsync(request);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("SignerCertDer required");
    }
}
