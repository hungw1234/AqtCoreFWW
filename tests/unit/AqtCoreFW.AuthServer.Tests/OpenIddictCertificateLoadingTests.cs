using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AqtCoreFW.AuthServer.Tests;

/// <summary>
/// Tests for <see cref="Program.LoadRequiredCertificate"/> — the non-Development OpenIddict
/// certificate path. Negative-first: a missing path or missing file must THROW (an OIDC server
/// silently falling back to ephemeral keys would invalidate all tokens per restart and mask a
/// broken Secret mount); positive: a real PFX with password loads.
/// </summary>
public sealed class OpenIddictCertificateLoadingTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    // ── Fail-fast branches ─────────────────────────────────────────────────

    [Fact]
    public void MissingPathConfig_Throws_NamingTheConfigKey()
    {
        var act = () => Program.LoadRequiredCertificate(Config(), "Auth:SigningCertificate");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:SigningCertificate:Path*");
    }

    [Fact]
    public void WhitespacePathConfig_Throws()
    {
        var act = () => Program.LoadRequiredCertificate(
            Config(("Auth:SigningCertificate:Path", "   ")), "Auth:SigningCertificate");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NonexistentFile_Throws_NamingThePath()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.pfx");

        var act = () => Program.LoadRequiredCertificate(
            Config(("Auth:EncryptionCertificate:Path", missing)), "Auth:EncryptionCertificate");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{missing}*");
    }

    // ── Positive branch ────────────────────────────────────────────────────

    [Fact]
    public void ValidPfxWithPassword_Loads()
    {
        var pfxPath = Path.Combine(Path.GetTempPath(), $"test-cert-{Guid.NewGuid():N}.pfx");
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=aqt-test-signing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx, "test-password"));

            var loaded = Program.LoadRequiredCertificate(
                Config(
                    ("Auth:SigningCertificate:Path", pfxPath),
                    ("Auth:SigningCertificate:Password", "test-password")),
                "Auth:SigningCertificate");

            loaded.Subject.Should().Be("CN=aqt-test-signing");
            loaded.HasPrivateKey.Should().BeTrue("OpenIddict signing needs the private key");
        }
        finally
        {
            File.Delete(pfxPath);
        }
    }
}
