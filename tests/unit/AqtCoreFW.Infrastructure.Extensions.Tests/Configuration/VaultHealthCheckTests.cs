using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VaultSharp;
using VaultSharp.V1.SystemBackend;

// Disambiguate: tests assert on Microsoft's HealthStatus enum; Vault returns VaultSharp's HealthStatus class.
using CheckStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;
using VaultHealth = VaultSharp.V1.SystemBackend.HealthStatus;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Configuration;

/// <summary>
/// Unit tests for VaultHealthCheck.
///
/// Coverage:
///   - Vault healthy (initialized, not sealed) → Healthy
///   - Vault sealed → Unhealthy "Vault sealed"
///   - Vault not initialized → Unhealthy "Vault not initialized"
///   - Vault unreachable in Development → Degraded
///   - Vault unreachable in Production → Unhealthy
/// </summary>
public sealed class VaultHealthCheckTests
{
    private readonly IVaultClient _client = Substitute.For<IVaultClient>();

    private readonly HealthCheckContext _context = new()
    {
        Registration = new HealthCheckRegistration("vault", _ => null!, null, null)
    };

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IWebHostEnvironment FakeEnv(string envName)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(envName);
        return env;
    }

    private VaultHealthCheck BuildCheck(string envName = "Production") =>
        new(_client, FakeEnv(envName), NullLogger<VaultHealthCheck>.Instance);

    private void SetupVaultHealth(bool initialized, bool sealed_) =>
        _client.V1.System
            .GetHealthStatusAsync(
                Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<System.Net.Http.HttpMethod>())
            .Returns(Task.FromResult(new VaultHealth
            {
                Initialized = initialized,
                Sealed = sealed_
            }));

    private void SetupVaultThrows() =>
        _client.V1.System
            .GetHealthStatusAsync(
                Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<System.Net.Http.HttpMethod>())
            .Throws(new HttpRequestException("Connection refused"));

    // ── Vault healthy ────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealth_InitializedAndUnsealed_ReturnsHealthy()
    {
        SetupVaultHealth(initialized: true, sealed_: false);

        var result = await BuildCheck().CheckHealthAsync(_context);

        result.Status.Should().Be(CheckStatus.Healthy);
    }

    // ── Vault sealed ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealth_VaultSealed_ReturnsUnhealthy()
    {
        SetupVaultHealth(initialized: true, sealed_: true);

        var result = await BuildCheck().CheckHealthAsync(_context);

        result.Status.Should().Be(CheckStatus.Unhealthy);
        result.Description.Should().Contain("sealed");
    }

    // ── Vault not initialized ────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealth_NotInitialized_ReturnsUnhealthy()
    {
        SetupVaultHealth(initialized: false, sealed_: false);

        var result = await BuildCheck().CheckHealthAsync(_context);

        result.Status.Should().Be(CheckStatus.Unhealthy);
        result.Description.Should().Contain("not initialized");
    }

    // ── Unreachable in Development → Degraded ───────────────────────────────

    [Fact]
    public async Task CheckHealth_UnreachableInDevelopment_ReturnsDegraded()
    {
        SetupVaultThrows();

        var result = await BuildCheck("Development").CheckHealthAsync(_context);

        result.Status.Should().Be(CheckStatus.Degraded);
        result.Description.Should().Contain("dev");
    }

    // ── Unreachable in Production → Unhealthy ────────────────────────────────

    [Fact]
    public async Task CheckHealth_UnreachableInProduction_ReturnsUnhealthy()
    {
        SetupVaultThrows();

        var result = await BuildCheck("Production").CheckHealthAsync(_context);

        result.Status.Should().Be(CheckStatus.Unhealthy);
    }
}
