using System.Text;
using AqtCoreFW.Audit.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VaultSharp;
using VaultSharp.V1.Commons;
using Xunit;

namespace AqtCoreFW.Audit.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for VaultAuditHmacKeyResolver (P5 of audit-hmac-vault-seeder plan).
///
/// Coverage:
///   - Happy path: Vault returns base64 32B → resolver returns matching bytes
///   - Cache hit: second call skips Vault read
///   - Length mismatch: 31-byte Vault value → InvalidOperationException (no legacy downgrade)
///   - Vault miss + no legacy config → KeyNotFoundException
///   - Vault miss + legacy Base64 config → returns decoded bytes (warn-once)
///   - Vault miss + legacy UTF-8 config → returns UTF-8 bytes (warn-once)
/// </summary>
public sealed class VaultAuditHmacKeyResolverTests : IDisposable
{
    private readonly IVaultClient _vaultClient = Substitute.For<IVaultClient>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 16 });

    public void Dispose() => _cache.Dispose();

    private VaultAuditHmacKeyResolver BuildResolver(IConfiguration? config = null) =>
        new(
            _vaultClient,
            _cache,
            config ?? new ConfigurationBuilder().Build(),
            NullLogger<VaultAuditHmacKeyResolver>.Instance);

    private static Secret<SecretData> MakeVaultSecret(Dictionary<string, object> data) =>
        new() { Data = new SecretData { Data = data } };

    private static byte[] Random32(int seed)
    {
        var bytes = new byte[32];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static IConfiguration ConfigWithLegacy(string legacyValue) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Audit:HmacKey"] = legacyValue })
            .Build();

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void GetKey_VaultHit_ReturnsBytes()
    {
        var keyBytes = Random32(1);
        var encoded = Convert.ToBase64String(keyBytes);

        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(Task.FromResult(MakeVaultSecret(new() { ["value"] = encoded })));

        var result = BuildResolver().GetKey();

        result.Should().Equal(keyBytes);
    }

    // ── Cache hit ────────────────────────────────────────────────────────────

    [Fact]
    public void GetKey_CacheHit_SkipsVault()
    {
        var keyBytes = Random32(2);
        var encoded = Convert.ToBase64String(keyBytes);

        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(Task.FromResult(MakeVaultSecret(new() { ["value"] = encoded })));

        var resolver = BuildResolver();
        resolver.GetKey();
        resolver.GetKey();

        _vaultClient.V1.Secrets.KeyValue.V2.Received(1)
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>());
    }

    // ── Length mismatch ─────────────────────────────────────────────────────
    //
    // CRITICAL invariant: a 31-byte Vault value must NEVER downgrade to legacy config.
    // That would silently hide a misconfiguration. Plan §P1 explicitly requires this.

    [Fact]
    public void GetKey_VaultLengthMismatch_Throws_DoesNotFallbackToLegacy()
    {
        var shortBytes = new byte[31];
        new Random(3).NextBytes(shortBytes);
        var encoded = Convert.ToBase64String(shortBytes);

        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(Task.FromResult(MakeVaultSecret(new() { ["value"] = encoded })));

        var legacyKey = Convert.ToBase64String(Random32(99));
        var act = () => BuildResolver(ConfigWithLegacy(legacyKey)).GetKey();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*31 bytes*32*");
    }

    // ── Vault miss + no legacy → throws ─────────────────────────────────────

    [Fact]
    public void GetKey_VaultMissAndNoLegacyConfig_Throws()
    {
        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Throws(new HttpRequestException("Connection refused"));

        var act = () => BuildResolver().GetKey();

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*hmac*")
            .WithInnerException<HttpRequestException>();
    }

    // ── Vault miss + legacy Base64 → returns decoded ────────────────────────

    [Fact]
    public void GetKey_VaultMissWithLegacyBase64_UsesLegacy()
    {
        var keyBytes = Random32(4);
        var legacyBase64 = Convert.ToBase64String(keyBytes);

        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Throws(new HttpRequestException("Vault unreachable"));

        var result = BuildResolver(ConfigWithLegacy(legacyBase64)).GetKey();

        result.Should().Equal(keyBytes);
    }

    // ── Vault miss + legacy UTF-8 (non-base64) → returns UTF-8 bytes ────────

    [Fact]
    public void GetKey_VaultMissWithUtf8Legacy_UsesUtf8()
    {
        // ":" is not valid base64; FromBase64String throws → fallback to UTF-8.
        const string legacyUtf8 = "legacy-plain-text-key:abc";
        var expected = Encoding.UTF8.GetBytes(legacyUtf8);

        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Throws(new HttpRequestException("Vault unreachable"));

        var result = BuildResolver(ConfigWithLegacy(legacyUtf8)).GetKey();

        result.Should().Equal(expected);
    }
}
