using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="TrustRootLoader"/>.
/// </summary>
[Trait("Category", "TrustRootLoader")]
public sealed class TrustRootLoaderTests : IDisposable
{
    // Temp files created per test — cleaned up in Dispose
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { /* best-effort */ }
        }
    }

    // -------------------------------------------------------------------------
    // 1. Valid DER file → returns raw bytes unchanged
    // -------------------------------------------------------------------------
    [Fact]
    public void LoadDer_ValidDerFile_ReturnsBytesUnchanged()
    {
        var der = GenerateSelfSignedDer("CN=TestRoot1");
        var path = WriteTempFile(der, ".der");

        var result = TrustRootLoader.LoadDer([path], NullLogger.Instance);

        result.Should().HaveCount(1);
        result[0].Should().Equal(der);
    }

    // -------------------------------------------------------------------------
    // 2. Missing path → skipped, empty result, no throw
    // -------------------------------------------------------------------------
    [Fact]
    public void LoadDer_MissingPath_SkipsWithoutThrowing_ReturnsEmpty()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".der");

        var act = () => TrustRootLoader.LoadDer([missingPath], NullLogger.Instance);

        act.Should().NotThrow();
        var result = TrustRootLoader.LoadDer([missingPath], NullLogger.Instance);
        result.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // 3. PEM + DER mix → both returned
    // -------------------------------------------------------------------------
    [Fact]
    public void LoadDer_PemAndDerMix_ReturnsBothAsBytes()
    {
        var der1 = GenerateSelfSignedDer("CN=TestRoot2");
        var der2 = GenerateSelfSignedDer("CN=TestRoot3");

        // Write one as DER file (raw bytes), one as PEM (base64-encoded with headers)
        var derPath = WriteTempFile(der1, ".cer");
        var pemContent = "-----BEGIN CERTIFICATE-----\n"
            + Convert.ToBase64String(der2, Base64FormattingOptions.InsertLineBreaks)
            + "\n-----END CERTIFICATE-----\n";
        var pemPath = WriteTempFile(System.Text.Encoding.ASCII.GetBytes(pemContent), ".pem");

        var result = TrustRootLoader.LoadDer([derPath, pemPath], NullLogger.Instance);

        result.Should().HaveCount(2, "both PEM and DER files should be loaded");
    }

    // -------------------------------------------------------------------------
    // 4. Empty path list → empty result, no throw
    // -------------------------------------------------------------------------
    [Fact]
    public void LoadDer_EmptyPaths_ReturnsEmpty()
    {
        var result = TrustRootLoader.LoadDer([], NullLogger.Instance);
        result.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // 5. One valid + one missing → returns only the valid one, no throw
    // -------------------------------------------------------------------------
    [Fact]
    public void LoadDer_OneValidOneMissing_ReturnsValidOnly()
    {
        var der = GenerateSelfSignedDer("CN=TestRoot4");
        var validPath = WriteTempFile(der, ".der");
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".der");

        var result = TrustRootLoader.LoadDer([validPath, missingPath], NullLogger.Instance);

        result.Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Generates a self-signed DER certificate for testing.</summary>
    private static byte[] GenerateSelfSignedDer(string subject)
    {
        var random = new SecureRandom();
        var gen = new RsaKeyPairGenerator();
        gen.Init(new Org.BouncyCastle.Crypto.Parameters.RsaKeyGenerationParameters(
            BigInteger.ValueOf(65537), random, 1024, 80));
        var kp = gen.GenerateKeyPair();

        var certGen = new X509V3CertificateGenerator();
        certGen.SetSerialNumber(BigInteger.ProbablePrime(64, random));
        var dn = new X509Name(subject);
        certGen.SetSubjectDN(dn);
        certGen.SetIssuerDN(dn);
        certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
        certGen.SetPublicKey(kp.Public);
        certGen.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));

        var sigFactory = new Asn1SignatureFactory("SHA256WithRSA", kp.Private, random);
        return certGen.Generate(sigFactory).GetEncoded();
    }

    /// <summary>Writes bytes to a temp file and registers it for cleanup. Returns the absolute path.</summary>
    private string WriteTempFile(byte[] content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }
}
