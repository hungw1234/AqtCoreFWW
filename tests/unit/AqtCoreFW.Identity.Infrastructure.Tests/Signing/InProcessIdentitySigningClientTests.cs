using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="InProcessIdentitySigningClient"/> — the cross-module verify façade.
/// Covers the trust-boundary mapping (Domain VerifyResult → contract evidence DTO), OCSP-mode
/// fail-safe resolution, and the fail-closed behaviour for misconfiguration (unknown provider/format).
/// </summary>
public sealed class InProcessIdentitySigningClientTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string ProviderKey = "remote-viettel-mysign";

    private readonly ISignatureProviderResolver _resolver = Substitute.For<ISignatureProviderResolver>();
    private readonly ITenantSignatureConfigRepository _configRepo = Substitute.For<ITenantSignatureConfigRepository>();
    private readonly ISignerBindingGuard _signerBindingGuard = Substitute.For<ISignerBindingGuard>();

    private InProcessIdentitySigningClient BuildSut() => new(_resolver, _configRepo, _signerBindingGuard);

    private static TenantSignatureConfig Config(OcspMode mode, bool enabled, string providerKey = ProviderKey) =>
        TenantSignatureConfig.Create(TenantId, providerKey, null, string.Empty, mode, enabled, SignatureFlowMode.Async, Guid.NewGuid());

    private static SignatureVerifyRequest RawRequest() => new()
    {
        Payload = [1, 2, 3],
        Signature = [9, 8, 7],
        SignerCertDer = [4, 5, 6],
        Format = "Raw",
        HashAlg = "SHA256",
        Encode = "RSA",
        ProviderKey = ProviderKey,
        TenantId = TenantId,
    };

    private static CertificateInfo Cert(string subject, string serial) =>
        new(serial, subject, "CN=Issuer CA", new DateTime(2026, 1, 1), new DateTime(2027, 1, 1), "AABBCC", [0x30, 0x82]);

    private IDigitalSignatureProvider StubProvider(VerifyResult result)
    {
        var provider = Substitute.For<IDigitalSignatureProvider>();
        provider.VerifyAsync(Arg.Any<VerifyRequest>(), Arg.Any<CancellationToken>()).Returns(result);
        _resolver.Resolve(ProviderKey).Returns(provider);
        return provider;
    }

    // (a) valid path — maps signer fields + non-vacuous chain evidence ────────────
    [Fact]
    public async Task VerifySignatureAsync_ValidSignature_MapsSignerAndChainEvidence()
    {
        var leaf = Cert("CN=Nguyen Van A", "0A1B2C");
        var chain = new ChainValidationResult(
            IsValid: true, FailureReason: null,
            Chain: [leaf, Cert("CN=Issuer CA", "FF00")],
            OcspChecked: true, OcspSucceeded: true);
        StubProvider(new VerifyResult(IsValid: true, FailureReason: null, SigningCertificate: leaf, ChainResult: chain));
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Config(OcspMode.Hard, enabled: true));

        var result = await BuildSut().VerifySignatureAsync(RawRequest());

        result.IsValid.Should().BeTrue();
        result.SignerSubject.Should().Be("CN=Nguyen Van A");
        result.SignerSerial.Should().Be("0A1B2C");
        result.SignerCertDer.Should().Equal(leaf.RawData);
        result.ChainEvidenceJson.Should().NotBeNullOrEmpty();

        // Non-vacuous: the serialized evidence carries the OCSP flags + every chain cert subject.
        var evidence = JsonSerializer.Deserialize<ChainEvidenceDto>(result.ChainEvidenceJson!)!;
        evidence.OcspChecked.Should().BeTrue();
        evidence.OcspSucceeded.Should().BeTrue();
        evidence.Chain.Select(c => c.Subject).Should().Contain(["CN=Nguyen Van A", "CN=Issuer CA"]);
        evidence.Chain.First().RawDataBase64.Should().Be(Convert.ToBase64String(leaf.RawData));
    }

    // (b) invalid path — passthrough IsValid=false + reason ───────────────────────
    [Fact]
    public async Task VerifySignatureAsync_ProviderReportsInvalid_PassesThroughFailure()
    {
        StubProvider(new VerifyResult(IsValid: false, FailureReason: "bad-sig", SigningCertificate: null, ChainResult: null));
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Config(OcspMode.Soft, enabled: true));

        var result = await BuildSut().VerifySignatureAsync(RawRequest());

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("bad-sig");
        result.ChainEvidenceJson.Should().BeNull();
    }

    // (c) provider not registered — KeyNotFound mapped to fail, no exception escapes ─
    [Fact]
    public async Task VerifySignatureAsync_ProviderNotRegistered_FailsClosedWithoutThrowing()
    {
        _resolver.Resolve(ProviderKey).Returns(_ => throw new KeyNotFoundException("no provider"));
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Config(OcspMode.Soft, enabled: true));

        var result = await BuildSut().VerifySignatureAsync(RawRequest());

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().StartWith("provider-not-registered");
    }

    // (d) config missing → OcspMode fail-safe Soft is what the provider receives ────
    [Fact]
    public async Task VerifySignatureAsync_NoConfig_PassesOcspModeSoftToProvider()
    {
        VerifyRequest? captured = null;
        var provider = Substitute.For<IDigitalSignatureProvider>();
        provider.VerifyAsync(Arg.Do<VerifyRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new VerifyResult(IsValid: true, FailureReason: null, SigningCertificate: null, ChainResult: null));
        _resolver.Resolve(ProviderKey).Returns(provider);
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantSignatureConfig?)null);

        await BuildSut().VerifySignatureAsync(RawRequest());

        captured.Should().NotBeNull();
        captured!.OcspMode.Should().Be(OcspMode.Soft);
    }

    // (d') config present but disabled → still fail-safe Soft ──────────────────────
    [Fact]
    public async Task VerifySignatureAsync_DisabledConfig_PassesOcspModeSoftToProvider()
    {
        VerifyRequest? captured = null;
        var provider = Substitute.For<IDigitalSignatureProvider>();
        provider.VerifyAsync(Arg.Do<VerifyRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new VerifyResult(IsValid: true, FailureReason: null, SigningCertificate: null, ChainResult: null));
        _resolver.Resolve(ProviderKey).Returns(provider);
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Config(OcspMode.Hard, enabled: false));

        await BuildSut().VerifySignatureAsync(RawRequest());

        captured!.OcspMode.Should().Be(OcspMode.Soft);
    }

    // (f) null request ProviderKey → falls back to the tenant config's provider ───
    [Fact]
    public async Task VerifySignatureAsync_NullRequestProviderKey_FallsBackToConfigProvider()
    {
        const string configProviderKey = "usb-vgca";
        var provider = Substitute.For<IDigitalSignatureProvider>();
        provider.VerifyAsync(Arg.Any<VerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new VerifyResult(IsValid: true, FailureReason: null, SigningCertificate: null, ChainResult: null));
        _resolver.Resolve(configProviderKey).Returns(provider);
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Config(OcspMode.Soft, enabled: true, providerKey: configProviderKey));

        var request = RawRequest();
        var noKeyRequest = new SignatureVerifyRequest
        {
            Payload = request.Payload, Signature = request.Signature, SignerCertDer = request.SignerCertDer,
            Format = request.Format, HashAlg = request.HashAlg, Encode = request.Encode,
            ProviderKey = null, TenantId = TenantId,
        };

        var result = await BuildSut().VerifySignatureAsync(noKeyRequest);

        result.IsValid.Should().BeTrue();
        await provider.Received(1).VerifyAsync(Arg.Any<VerifyRequest>(), Arg.Any<CancellationToken>());
    }

    // (g) no provider key anywhere (request null + no config) → fail-closed ─────────
    [Fact]
    public async Task VerifySignatureAsync_NoProviderKeyAnywhere_FailsClosedProviderNotConfigured()
    {
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantSignatureConfig?)null);

        var request = RawRequest();
        var noKeyRequest = new SignatureVerifyRequest
        {
            Payload = request.Payload, Signature = request.Signature, SignerCertDer = request.SignerCertDer,
            Format = request.Format, HashAlg = request.HashAlg, Encode = request.Encode,
            ProviderKey = null, TenantId = TenantId,
        };

        var result = await BuildSut().VerifySignatureAsync(noKeyRequest);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().StartWith("provider-not-configured");
        _resolver.DidNotReceive().Resolve(Arg.Any<string>());
    }

    // (e) unknown format string → fail-closed, no enum-parse crash, provider never called ─
    [Fact]
    public async Task VerifySignatureAsync_UnknownFormat_FailsClosedWithoutCallingProvider()
    {
        var provider = StubProvider(new VerifyResult(IsValid: true, FailureReason: null, SigningCertificate: null, ChainResult: null));
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Config(OcspMode.Soft, enabled: true));

        var request = RawRequest();
        var badFormatRequest = new SignatureVerifyRequest
        {
            Payload = request.Payload, Signature = request.Signature, SignerCertDer = request.SignerCertDer,
            Format = "Cms-Bogus", HashAlg = request.HashAlg, Encode = request.Encode,
            ProviderKey = request.ProviderKey, TenantId = request.TenantId,
        };

        var result = await BuildSut().VerifySignatureAsync(badFormatRequest);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().StartWith("unknown-format");
        await provider.DidNotReceive().VerifyAsync(Arg.Any<VerifyRequest>(), Arg.Any<CancellationToken>());
    }

    // (h) binding guard rejects cert → fail-closed with signer-binding-failed reason ─
    [Fact]
    public async Task VerifySignatureAsync_BindingGuardReturnsNotBound_FailsClosedWithBindingError()
    {
        var leaf = Cert("CN=Nguyen Van A", "0A1B2C");
        var chain = new ChainValidationResult(
            IsValid: true, FailureReason: null,
            Chain: [leaf, Cert("CN=Issuer CA", "FF00")],
            OcspChecked: true, OcspSucceeded: true);
        StubProvider(new VerifyResult(IsValid: true, FailureReason: null, SigningCertificate: leaf, ChainResult: chain));
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Config(OcspMode.Hard, enabled: true));

        // Signer binding guard rejects the certificate
        _signerBindingGuard.VerifyAsync(Arg.Any<CertificateInfo>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(SignerBindingVerdict.NotBound("cert mismatch"));

        var request = RawRequest();
        var withSignerRequest = new SignatureVerifyRequest
        {
            Payload = request.Payload, Signature = request.Signature, SignerCertDer = request.SignerCertDer,
            Format = request.Format, HashAlg = request.HashAlg, Encode = request.Encode,
            ProviderKey = request.ProviderKey, TenantId = request.TenantId,
            SignerUserId = Guid.NewGuid(), // Presence of SignerUserId triggers binding check
        };

        var result = await BuildSut().VerifySignatureAsync(withSignerRequest);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().StartWith("signer-binding-failed:");
    }

    // (i) binding guard not consulted when SignerUserId is null ─────────────────────
    [Fact]
    public async Task VerifySignatureAsync_NoSignerUserId_BindingGuardNotCalled()
    {
        var leaf = Cert("CN=Nguyen Van A", "0A1B2C");
        var chain = new ChainValidationResult(
            IsValid: true, FailureReason: null,
            Chain: [leaf, Cert("CN=Issuer CA", "FF00")],
            OcspChecked: true, OcspSucceeded: true);
        StubProvider(new VerifyResult(IsValid: true, FailureReason: null, SigningCertificate: leaf, ChainResult: chain));
        _configRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Config(OcspMode.Soft, enabled: true));

        var result = await BuildSut().VerifySignatureAsync(RawRequest()); // No SignerUserId

        result.IsValid.Should().BeTrue();
        await _signerBindingGuard.DidNotReceive().VerifyAsync(
            Arg.Any<CertificateInfo>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
