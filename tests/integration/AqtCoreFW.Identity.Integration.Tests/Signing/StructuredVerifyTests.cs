using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// W2 integration tests for <see cref="StructuredSignatureService"/> — XMLDSig + JWS paths.
///
/// Strategy:
/// <list type="bullet">
///   <item>Generate a self-signed RSA-2048 cert via .NET native API for every test class instance.</item>
///   <item>Mock <see cref="ICertificateChainValidator"/> to return IsValid=true — isolates structured
///     signature logic from chain/OCSP (tested separately in W3).</item>
///   <item>Six cases: XMLDSig happy, XMLDSig tampered, JWS happy, JWS tampered, alg=none rejected,
///     malformed JWS rejected.</item>
/// </list>
/// </summary>
public sealed class StructuredVerifyTests : IDisposable
{
    private readonly RSA _rsa;
    private readonly X509Certificate2 _signerCert;
    private readonly ICertificateChainValidator _chainValidator;
    private readonly StructuredSignatureService _sut;

    private static readonly byte[] SamplePayload = "w2-structured-verify-test-2026"u8.ToArray();

    public StructuredVerifyTests()
    {
        _rsa = RSA.Create(2048);

        var req = new CertificateRequest(
            "CN=VGCA Test Signer W2, O=AqtCoreFW Test CA, C=VN",
            _rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, critical: true));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        _signerCert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // Mock chain validator — returns IsValid=true for all structured verify tests
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

        _sut = new StructuredSignatureService(
            _chainValidator,
            NullLogger<StructuredSignatureService>.Instance);
    }

    public void Dispose()
    {
        _signerCert.Dispose();
        _rsa.Dispose();
    }

    // ─── XMLDSig helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a valid XMLDSig enveloped document over the given payload text.
    /// Uses System.Security.Cryptography.Xml.SignedXml (same library used by StructuredSignatureService).
    /// </summary>
    private byte[] BuildXmlDsig(string payloadXml, RSA key, X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(payloadXml);

        var signedXml = new SignedXml(doc);
        signedXml.SigningKey = key;
        signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;

        // Enveloped reference — covers entire document
        var reference = new Reference { Uri = "" };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);

        // Embed certificate in KeyInfo
        var keyInfo = new KeyInfo();
        var x509Data = new KeyInfoX509Data(cert);
        keyInfo.AddClause(x509Data);
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        var xmlSig = signedXml.GetXml();
        doc.DocumentElement!.AppendChild(doc.ImportNode(xmlSig, deep: true));

        return Encoding.UTF8.GetBytes(doc.OuterXml);
    }

    // ─── JWS helpers ─────────────────────────────────────────────────────────

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// Builds a JWS compact serialization (RS256) with x5c cert chain header.
    /// </summary>
    private string BuildJwsCompact(byte[] payloadBytes, RSA key, X509Certificate2 cert, string alg = "RS256")
    {
        // Header — x5c values use standard base64 (NOT base64url) per RFC 7515 §4.1.6
        var header = new
        {
            alg,
            typ = "JWT",
            x5c = new[] { Convert.ToBase64String(cert.RawData) },
        };
        var headerJson   = JsonSerializer.Serialize(header);
        var headerSeg    = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadSeg   = Base64UrlEncode(payloadBytes);

        var signingInput = Encoding.ASCII.GetBytes(headerSeg + "." + payloadSeg);
        var signature    = key.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sigSeg       = Base64UrlEncode(signature);

        return $"{headerSeg}.{payloadSeg}.{sigSeg}";
    }

    // ─── Test 1: XMLDSig happy path ───────────────────────────────────────────

    [Fact(DisplayName = "XMLDSig verify: valid signed document returns IsValid=true")]
    public async Task VerifyXmlDsig_ValidDocument_ReturnsIsValidTrue()
    {
        // Arrange
        var xmlDsigBytes = BuildXmlDsig(
            "<Root><Data>hello W2</Data></Root>", _rsa, _signerCert);

        // Act
        var result = await _sut.VerifyXmlDsigAsync(xmlDsigBytes, OcspMode.Soft);

        // Assert
        result.IsValid.Should().BeTrue(because: "document was signed correctly");
        result.FailureReason.Should().BeNull();
        result.SigningCertificate.Should().NotBeNull();
        result.SigningCertificate!.Subject.Should().Contain("VGCA Test Signer W2");
        result.ChainResult.Should().NotBeNull();
    }

    // ─── Test 2: XMLDSig tampered content ─────────────────────────────────────

    [Fact(DisplayName = "XMLDSig verify: tampered document returns IsValid=false")]
    public async Task VerifyXmlDsig_TamperedDocument_ReturnsIsValidFalse()
    {
        // Arrange — sign then modify the XML content after signing
        var xmlDsigBytes = BuildXmlDsig(
            "<Root><Data>hello W2</Data></Root>", _rsa, _signerCert);

        var xml = Encoding.UTF8.GetString(xmlDsigBytes);
        var tampered = xml.Replace("hello W2", "tampered!");
        var tamperedBytes = Encoding.UTF8.GetBytes(tampered);

        // Act
        var result = await _sut.VerifyXmlDsigAsync(tamperedBytes, OcspMode.Soft);

        // Assert
        result.IsValid.Should().BeFalse(because: "document content was modified after signing");
        result.FailureReason.Should().NotBeNullOrEmpty()
            .And.ContainAny(["invalid", "Invalid", "signature", "Signature"],
                because: "failure reason must identify a signature/validity problem, not a parse error");
    }

    // ─── Test 3: JWS happy path ────────────────────────────────────────────────

    [Fact(DisplayName = "JWS verify: valid RS256 compact returns IsValid=true")]
    public async Task VerifyJws_ValidRS256_ReturnsIsValidTrue()
    {
        // Arrange
        var jws = BuildJwsCompact(SamplePayload, _rsa, _signerCert);

        // Act
        var result = await _sut.VerifyJwsAsync(jws, OcspMode.Soft);

        // Assert
        result.IsValid.Should().BeTrue(because: "JWS was signed correctly with the cert's private key");
        result.FailureReason.Should().BeNull();
        result.SigningCertificate.Should().NotBeNull();
        result.ChainResult.Should().NotBeNull();
    }

    // ─── Test 4: JWS tampered payload ─────────────────────────────────────────

    [Fact(DisplayName = "JWS verify: tampered payload segment returns IsValid=false")]
    public async Task VerifyJws_TamperedPayload_ReturnsIsValidFalse()
    {
        // Arrange — build valid JWS, then replace the payload segment with something else
        var jws = BuildJwsCompact(SamplePayload, _rsa, _signerCert);
        var parts = jws.Split('.');
        var fakePayload = Base64UrlEncode("tampered-payload"u8.ToArray());
        var tampered = $"{parts[0]}.{fakePayload}.{parts[2]}";

        // Act
        var result = await _sut.VerifyJwsAsync(tampered, OcspMode.Soft);

        // Assert
        result.IsValid.Should().BeFalse(because: "JWS payload was modified after signing");
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    // ─── Test 5: alg=none rejected (CVE-2015-9235) ────────────────────────────

    [Fact(DisplayName = "JWS verify: alg=none is rejected unconditionally")]
    public async Task VerifyJws_AlgNone_RejectedBeforeCrypto()
    {
        // Arrange — craft a JWS with alg=none (no signature bytes needed by spec)
        var header    = JsonSerializer.Serialize(new { alg = "none", typ = "JWT" });
        var headerSeg = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        var payload   = Base64UrlEncode(SamplePayload);
        var compact   = $"{headerSeg}.{payload}."; // empty signature segment

        // Act
        var result = await _sut.VerifyJwsAsync(compact, OcspMode.Soft);

        // Assert
        result.IsValid.Should().BeFalse(because: "alg=none must be rejected to prevent CVE-2015-9235 class attacks");
        result.FailureReason.Should().Contain("none");
    }

    // ─── Test 6: malformed JWS ─────────────────────────────────────────────────

    [Fact(DisplayName = "JWS verify: malformed compact (2 segments) returns IsValid=false")]
    public async Task VerifyJws_MalformedCompact_ReturnsIsValidFalse()
    {
        // Arrange — only 2 segments instead of 3
        var malformed = "header.payload";

        // Act
        var result = await _sut.VerifyJwsAsync(malformed, OcspMode.Soft);

        // Assert
        result.IsValid.Should().BeFalse(because: "JWS compact must have exactly 3 segments");
        result.FailureReason.Should().Contain("3 segments");
    }
}
