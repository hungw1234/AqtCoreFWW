using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VaultSharp;
using VaultSharp.V1.Commons;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Configuration;

/// <summary>
/// Unit tests for VaultPepperResolver.
///
/// Coverage:
///   - Happy path: Vault returns valid base64 pepper → decoded bytes returned
///   - Vault miss (null secret data) → KeyNotFoundException
///   - Cache hit bypasses Vault on second call
///   - Secret present but "value" key absent → KeyNotFoundException
///   - Vault client throws → wrapped as KeyNotFoundException
/// </summary>
public sealed class VaultPepperResolverTests : IDisposable
{
    private readonly IVaultClient _vaultClient = Substitute.For<IVaultClient>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private static readonly Guid TenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public void Dispose() => _cache.Dispose();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private VaultPepperResolver BuildResolver() =>
        new(_vaultClient, _cache, NullLogger<VaultPepperResolver>.Instance);

    private static Secret<SecretData> MakeVaultSecret(Dictionary<string, object> data) =>
        new() { Data = new SecretData { Data = data } };

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void GetPepper_ReturnsBytes_WhenVaultHasSecret()
    {
        var pepperBytes = new byte[32];
        new Random(1).NextBytes(pepperBytes);
        var encoded = Convert.ToBase64String(pepperBytes);

        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(Task.FromResult(MakeVaultSecret(new() { ["value"] = encoded })));

        var result = BuildResolver().GetPepper(TenantId);

        result.Should().Equal(pepperBytes);
    }

    // ── Vault miss → KeyNotFoundException ───────────────────────────────────

    [Fact]
    public void GetPepper_ThrowsKeyNotFoundException_WhenVaultMisses()
    {
        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(Task.FromResult<Secret<SecretData>>(new Secret<SecretData> { Data = null! }));

        var act = () => BuildResolver().GetPepper(TenantId);

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage($"*{TenantId:N}*");
    }

    // ── Cache hit bypasses Vault ─────────────────────────────────────────────

    [Fact]
    public void GetPepper_CachesResult_AcrossCalls()
    {
        var pepperBytes = new byte[32];
        new Random(2).NextBytes(pepperBytes);
        var encoded = Convert.ToBase64String(pepperBytes);

        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(Task.FromResult(MakeVaultSecret(new() { ["value"] = encoded })));

        var resolver = BuildResolver();
        resolver.GetPepper(TenantId);
        resolver.GetPepper(TenantId);

        // Vault should be called exactly once; second call hits cache
        _vaultClient.V1.Secrets.KeyValue.V2.Received(1)
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>());
    }

    // ── "value" key absent → KeyNotFoundException ────────────────────────────

    [Fact]
    public void GetPepper_ThrowsKeyNotFoundException_WhenSecretMissingValueKey()
    {
        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(Task.FromResult(MakeVaultSecret(new() { ["other"] = "irrelevant" })));

        var act = () => BuildResolver().GetPepper(TenantId);

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage($"*{TenantId:N}*");
    }

    // ── Vault client throws → wrapped as KeyNotFoundException ────────────────

    [Fact]
    public void GetPepper_ThrowsKeyNotFoundException_WhenVaultClientThrows()
    {
        _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Throws(new HttpRequestException("Connection refused"));

        var act = () => BuildResolver().GetPepper(TenantId);

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage($"*{TenantId:N}*")
            .WithInnerException<HttpRequestException>();
    }
}
