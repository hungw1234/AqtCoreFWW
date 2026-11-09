using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="CertificateChainValidator"/> (QĐ 742 §5.1 W3).
/// Uses a self-signed test CA generated at fixture init — no live OCSP/network calls.
/// </summary>
[Trait("Category", "CertificateChainValidator")]
public sealed class CertificateChainValidatorTests : IClassFixture<TestCaFixture>
{
    private readonly TestCaFixture _ca;

    public CertificateChainValidatorTests(TestCaFixture ca) => _ca = ca;

    // -------------------------------------------------------------------------
    // 1. Valid chain → IsValid=true, OcspChecked=false (no OCSP client injected)
    // -------------------------------------------------------------------------
    [Fact]
    public async Task ValidChain_NoOcspClient_ReturnsValid_OcspCheckedFalse()
    {
        var (rootStore, validator) = BuildValidator();
        _ = rootStore; // used implicitly via VnCaRootStore seed

        var result = await validator.ValidateAsync(
            _ca.LeafCertDer,
            [_ca.IntermediateCertDer],
            OcspMode.Soft,
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.OcspChecked.Should().BeFalse("no IOcspClient was injected");
        result.OcspSucceeded.Should().BeFalse();
        result.FailureReason.Should().BeNull();
        result.Chain.Should().NotBeEmpty();
    }

    // -------------------------------------------------------------------------
    // 2. Untrusted root → IsValid=false, FailureReason mentions root pin
    // -------------------------------------------------------------------------
    [Fact]
    public async Task UntrustedRoot_ReturnsInvalid_RootPinMentionedInFailure()
    {
        // Build a DIFFERENT root CA and sign a cert with it — not seeded in VnCaRootStore
        var foreignCa = TestCaFixture.GenerateCaBundle("CN=ForeignCA");
        var rootStore = new VnCaRootStore([]); // empty — no roots trusted
        var validator = new CertificateChainValidator(rootStore, NullLogger<CertificateChainValidator>.Instance);

        var result = await validator.ValidateAsync(
            foreignCa.LeafCertDer,
            [foreignCa.IntermediateCertDer],
            OcspMode.Soft,
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().NotBeNull();
        // Must mention either root pin or no trusted roots
        (result.FailureReason!.Contains("root", StringComparison.OrdinalIgnoreCase) ||
         result.FailureReason.Contains("trust", StringComparison.OrdinalIgnoreCase) ||
         result.FailureReason.Contains("PRODUCTION_SEED_DEFERRED", StringComparison.Ordinal))
            .Should().BeTrue($"FailureReason was: {result.FailureReason}");
    }

    // -------------------------------------------------------------------------
    // 3. Expired cert → IsValid=false, FailureReason mentions expiry/validity
    // -------------------------------------------------------------------------
    [Fact]
    public async Task ExpiredLeafCert_ReturnsInvalid_ValidityMentionedInFailure()
    {
        var expiredDer = TestCaFixture.GenerateExpiredLeaf(_ca.RootKeyPair, _ca.RootCert);
        var (_, validator) = BuildValidator();

        var result = await validator.ValidateAsync(
            expiredDer,
            [],
            OcspMode.Soft,
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().NotBeNull();
        (result.FailureReason!.Contains("valid", StringComparison.OrdinalIgnoreCase) ||
         result.FailureReason.Contains("expir", StringComparison.OrdinalIgnoreCase) ||
         result.FailureReason.Contains("chain", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue($"FailureReason was: {result.FailureReason}");
    }

    // -------------------------------------------------------------------------
    // 4. Mock OCSP returns Revoked → IsValid=false BOTH modes
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(OcspMode.Soft)]
    [InlineData(OcspMode.Hard)]
    public async Task OcspRevoked_IsInvalid_BothModes(OcspMode mode)
    {
        var mockOcsp = Substitute.For<IOcspClient>();
        mockOcsp.CheckAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
                .Returns(OcspCertificateStatus.Revoked);

        var (_, validator) = BuildValidator(mockOcsp);

        var result = await validator.ValidateAsync(
            _ca.LeafCertDer,
            [_ca.IntermediateCertDer],
            mode,
            CancellationToken.None);

        result.IsValid.Should().BeFalse($"revoked cert must be rejected in {mode} mode");
        result.OcspChecked.Should().BeTrue();
        result.OcspSucceeded.Should().BeFalse();
        result.FailureReason.Should().Contain("evok");
    }

    // -------------------------------------------------------------------------
    // 5. Mock OCSP unreachable + mode=Soft → IsValid=true, OcspSucceeded=false
    // -------------------------------------------------------------------------
    [Fact]
    public async Task OcspUnreachable_SoftMode_IsValid_OcspSucceededFalse()
    {
        var mockOcsp = Substitute.For<IOcspClient>();
        mockOcsp.CheckAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
                .Returns(OcspCertificateStatus.Unreachable);

        var (_, validator) = BuildValidator(mockOcsp);

        var result = await validator.ValidateAsync(
            _ca.LeafCertDer,
            [_ca.IntermediateCertDer],
            OcspMode.Soft,
            CancellationToken.None);

        result.IsValid.Should().BeTrue("Soft mode tolerates unreachable OCSP");
        result.OcspChecked.Should().BeTrue();
        result.OcspSucceeded.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // 6. Mock OCSP unreachable + mode=Hard → IsValid=false
    // -------------------------------------------------------------------------
    [Fact]
    public async Task OcspUnreachable_HardMode_IsInvalid()
    {
        var mockOcsp = Substitute.For<IOcspClient>();
        mockOcsp.CheckAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
                .Returns(OcspCertificateStatus.Unreachable);

        var (_, validator) = BuildValidator(mockOcsp);

        var result = await validator.ValidateAsync(
            _ca.LeafCertDer,
            [_ca.IntermediateCertDer],
            OcspMode.Hard,
            CancellationToken.None);

        result.IsValid.Should().BeFalse("Hard mode rejects unreachable OCSP");
        result.OcspChecked.Should().BeTrue();
        result.OcspSucceeded.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // 7. Mock OCSP returns Good → IsValid=true, OcspSucceeded=true
    // -------------------------------------------------------------------------
    [Fact]
    public async Task OcspGood_IsValid_OcspSucceededTrue()
    {
        var mockOcsp = Substitute.For<IOcspClient>();
        mockOcsp.CheckAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
                .Returns(OcspCertificateStatus.Good);

        var (_, validator) = BuildValidator(mockOcsp);

        var result = await validator.ValidateAsync(
            _ca.LeafCertDer,
            [_ca.IntermediateCertDer],
            OcspMode.Hard,
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.OcspChecked.Should().BeTrue();
        result.OcspSucceeded.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // 8. Raw-path candidate pool — root in VnCaRootStore, intermediate in VnCaIntermediateStore,
    //    intermediates arg = null → chain closes via candidate pool → IsValid=true
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RawPath_IntermediateInCandidatePool_ChainClosesWithoutIntermediatesArg()
    {
        // Intermediates are path-building candidates, never trust anchors.
        // Simulates raw-path: provider passes intermediates:null (no caller-supplied chain).
        var rootStore = new VnCaRootStore([_ca.RootCertDer]);
        var intermediateStore = new VnCaIntermediateStore([_ca.IntermediateCertDer]);
        var validator = new CertificateChainValidator(
            rootStore,
            NullLogger<CertificateChainValidator>.Instance,
            ocspClient: null,
            intermediateStore: intermediateStore);

        var result = await validator.ValidateAsync(
            _ca.LeafCertDer,
            intermediates: null,    // raw-path: no caller-supplied intermediates
            OcspMode.Soft,
            CancellationToken.None);

        result.IsValid.Should().BeTrue("chain should close via candidate pool even with null intermediates arg");
        result.OcspChecked.Should().BeFalse("no OCSP client injected");
    }

    // -------------------------------------------------------------------------
    // 9. Pin-integrity — VnCaIntermediateStore contents are path-building candidates only.
    //    They must NOT be added to trustAnchors or the pin-set.
    //    If a cert is only in VnCaIntermediateStore (not in VnCaRootStore), the chain
    //    cannot anchor there — PKIX will fail because there is no trust anchor.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task PinIntegrity_IntermediateOnlyInCandidateStore_NotInRootStore_ChainFails()
    {
        // Empty VnCaRootStore (no trust anchors), intermediate only in VnCaIntermediateStore.
        // VnCaIntermediateStore candidates must NOT be promoted to trust anchors.
        // PKIX will fail with "no trust anchors" because the intermediate is a candidate, not an anchor.
        var emptyRootStore = new VnCaRootStore([]); // no anchors
        var intermediateStore = new VnCaIntermediateStore([_ca.IntermediateCertDer]);
        var validator = new CertificateChainValidator(
            emptyRootStore,
            NullLogger<CertificateChainValidator>.Instance,
            ocspClient: null,
            intermediateStore: intermediateStore);

        var result = await validator.ValidateAsync(
            _ca.LeafCertDer,
            intermediates: null,
            OcspMode.Soft,
            CancellationToken.None);

        // Must fail: no trust anchors configured (intermediate is not promoted to anchor)
        result.IsValid.Should().BeFalse("VnCaIntermediateStore candidates must never become trust anchors");
        result.FailureReason.Should().NotBeNull();
    }

    [Fact]
    public async Task PinIntegrity_ValidChain_PinSetContainsRootNotIntermediate()
    {
        // After successful raw-path validation, the VnCaRootStore pin-set must contain
        // only the ROOT thumbprint — not the intermediate. This proves the intermediate
        // was added to allCerts (path-building) only, never to the pin-set.
        var rootStore = new VnCaRootStore([_ca.RootCertDer]);
        var intermediateStore = new VnCaIntermediateStore([_ca.IntermediateCertDer]);
        var validator = new CertificateChainValidator(
            rootStore,
            NullLogger<CertificateChainValidator>.Instance,
            ocspClient: null,
            intermediateStore: intermediateStore);

        var result = await validator.ValidateAsync(
            _ca.LeafCertDer,
            intermediates: null,
            OcspMode.Soft,
            CancellationToken.None);

        result.IsValid.Should().BeTrue();

        var rootThumbprint = VnCaRootStore.ComputeThumbprint(_ca.RootCertDer);
        var intermediateThumbprint = VnCaRootStore.ComputeThumbprint(_ca.IntermediateCertDer);

        // Pin-set must include the root
        rootStore.GetTrustedRootThumbprints().Should().Contain(rootThumbprint,
            "root cert is seeded into VnCaRootStore — its thumbprint must be in the pin-set");

        // Pin-set must NOT include the intermediate (it was in VnCaIntermediateStore, not VnCaRootStore)
        rootStore.GetTrustedRootThumbprints().Should().NotContain(intermediateThumbprint,
            "intermediate is a path-building candidate only — it must not appear in the pin-set");
    }

    // -------------------------------------------------------------------------
    // 10. OCSP runs on raw-path — stub IOcspClient returns Good, root+intermediate
    //     seeded in their stores, intermediates arg = null → OcspChecked=true
    // -------------------------------------------------------------------------
    [Fact]
    public async Task OcspRunsOnRawPath_IssuerFoundFromBuiltChain_OcspCheckedTrue()
    {
        // When intermediates:null is passed (raw-path), the validator must source
        // the issuer cert from the PKIX-built chain (via VnCaIntermediateStore) so
        // OCSP can still run — not silently skip.
        var mockOcsp = Substitute.For<IOcspClient>();
        mockOcsp.CheckAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
                .Returns(OcspCertificateStatus.Good);

        var rootStore = new VnCaRootStore([_ca.RootCertDer]);
        var intermediateStore = new VnCaIntermediateStore([_ca.IntermediateCertDer]);
        var validator = new CertificateChainValidator(
            rootStore,
            NullLogger<CertificateChainValidator>.Instance,
            ocspClient: mockOcsp,
            intermediateStore: intermediateStore);

        var result = await validator.ValidateAsync(
            _ca.LeafCertDer,
            intermediates: null,    // raw-path
            OcspMode.Soft,
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.OcspChecked.Should().BeTrue("issuer is discoverable from built chain — OCSP must not silent-skip");
        result.OcspSucceeded.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Builds a validator seeded with the test CA root.</summary>
    private (VnCaRootStore, CertificateChainValidator) BuildValidator(IOcspClient? ocsp = null)
    {
        var rootStore = new VnCaRootStore([_ca.RootCertDer]);
        var validator = new CertificateChainValidator(
            rootStore,
            NullLogger<CertificateChainValidator>.Instance,
            ocsp);
        return (rootStore, validator);
    }
}

// =============================================================================
// Shared test CA fixture — generated once per test class run.
// Root CA → Intermediate CA → Leaf certificate (3-level chain).
// =============================================================================

/// <summary>
/// xunit class fixture that generates a self-signed root CA, intermediate CA,
/// and a leaf end-entity certificate using BouncyCastle <see cref="X509V3CertificateGenerator"/>
/// with SHA256WithRSA. All certs are valid for 10 years from generation time.
/// </summary>
public sealed class TestCaFixture
{
    public AsymmetricCipherKeyPair RootKeyPair { get; }
    public X509Certificate RootCert { get; }
    public byte[] RootCertDer { get; }

    public byte[] IntermediateCertDer { get; }
    public byte[] LeafCertDer { get; }

    public TestCaFixture()
    {
        var bundle = GenerateCaBundle("CN=TestVnCaRoot");
        RootKeyPair = bundle.RootKeyPair;
        RootCert = bundle.RootCert;
        RootCertDer = bundle.RootCertDer;
        IntermediateCertDer = bundle.IntermediateCertDer;
        LeafCertDer = bundle.LeafCertDer;
    }

    // -------------------------------------------------------------------------
    // Static factory — usable from individual test methods for foreign-CA scenarios
    // -------------------------------------------------------------------------

    public static CaBundle GenerateCaBundle(string rootSubject)
    {
        var random = new SecureRandom();

        // --- Root CA ---
        var rootKp = GenerateRsaKeyPair(random, 2048);
        var rootCert = BuildCert(
            subject: new X509Name(rootSubject),
            issuer: new X509Name(rootSubject),
            issuerKp: rootKp,
            subjectKp: rootKp,
            isCA: true,
            random: random);
        var rootDer = rootCert.GetEncoded();

        // --- Intermediate CA (signed by root) ---
        var intKp = GenerateRsaKeyPair(random, 2048);
        var intCert = BuildCert(
            subject: new X509Name("CN=TestVnCaIntermediate"),
            issuer: rootCert.SubjectDN,
            issuerKp: rootKp,
            subjectKp: intKp,
            isCA: true,
            random: random);
        var intDer = intCert.GetEncoded();

        // --- Leaf EE cert (signed by intermediate) ---
        var leafKp = GenerateRsaKeyPair(random, 2048);
        var leafCert = BuildCert(
            subject: new X509Name("CN=TestLeafCert"),
            issuer: intCert.SubjectDN,
            issuerKp: intKp,
            subjectKp: leafKp,
            isCA: false,
            random: random);
        var leafDer = leafCert.GetEncoded();

        return new CaBundle(rootKp, rootCert, rootDer, intDer, leafDer);
    }

    /// <summary>Generates a leaf cert with NotAfter in the past (expired).</summary>
    public static byte[] GenerateExpiredLeaf(
        AsymmetricCipherKeyPair issuerKp,
        X509Certificate issuerCert)
    {
        var random = new SecureRandom();
        var leafKp = GenerateRsaKeyPair(random, 2048);

        var gen = new X509V3CertificateGenerator();
        gen.SetSerialNumber(BigInteger.ProbablePrime(120, random));
        gen.SetSubjectDN(new X509Name("CN=ExpiredLeaf"));
        gen.SetIssuerDN(issuerCert.SubjectDN);
        // NotAfter 2 days ago → expired
        gen.SetNotBefore(DateTime.UtcNow.AddDays(-10));
        gen.SetNotAfter(DateTime.UtcNow.AddDays(-2));
        gen.SetPublicKey(leafKp.Public);

        var sigFactory = new Asn1SignatureFactory("SHA256WithRSA", issuerKp.Private, random);
        return gen.Generate(sigFactory).GetEncoded();
    }

    // -------------------------------------------------------------------------
    // BC helpers
    // -------------------------------------------------------------------------

    private static AsymmetricCipherKeyPair GenerateRsaKeyPair(SecureRandom random, int keySize)
    {
        var gen = new RsaKeyPairGenerator();
        gen.Init(new Org.BouncyCastle.Crypto.Parameters.RsaKeyGenerationParameters(
            BigInteger.ValueOf(65537), random, keySize, 80));
        return gen.GenerateKeyPair();
    }

    private static X509Certificate BuildCert(
        X509Name subject,
        X509Name issuer,
        AsymmetricCipherKeyPair issuerKp,
        AsymmetricCipherKeyPair subjectKp,
        bool isCA,
        SecureRandom random)
    {
        var gen = new X509V3CertificateGenerator();
        gen.SetSerialNumber(BigInteger.ProbablePrime(120, random));
        gen.SetSubjectDN(subject);
        gen.SetIssuerDN(issuer);
        gen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        gen.SetNotAfter(DateTime.UtcNow.AddYears(10));
        gen.SetPublicKey(subjectKp.Public);

        // Basic constraints: cA flag
        gen.AddExtension(
            X509Extensions.BasicConstraints,
            critical: true,
            new BasicConstraints(isCA));

        var sigFactory = new Asn1SignatureFactory("SHA256WithRSA", issuerKp.Private, random);
        return gen.Generate(sigFactory);
    }
}

/// <summary>Immutable bundle of DER bytes produced by <see cref="TestCaFixture.GenerateCaBundle"/>.</summary>
public sealed record CaBundle(
    AsymmetricCipherKeyPair RootKeyPair,
    X509Certificate RootCert,
    byte[] RootCertDer,
    byte[] IntermediateCertDer,
    byte[] LeafCertDer);
