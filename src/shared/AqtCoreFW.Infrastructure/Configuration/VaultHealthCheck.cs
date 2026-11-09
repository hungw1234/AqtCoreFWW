using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using VaultSharp;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Readiness health check for HashiCorp Vault.
/// Unhealthy when sealed or uninitialized.
/// Dev-mode: Vault unreachable → Degraded (not Unhealthy) so devs without Vault container see yellow, not red.
/// </summary>
public sealed class VaultHealthCheck(
    IVaultClient client,
    IWebHostEnvironment env,
    ILogger<VaultHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await client.V1.System.GetHealthStatusAsync();

            if (!health.Initialized)
                return HealthCheckResult.Unhealthy("Vault not initialized");

            if (health.Sealed)
                return HealthCheckResult.Unhealthy("Vault sealed");

            return HealthCheckResult.Healthy("Vault healthy");
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            logger.LogWarning(ex, "Vault unreachable");

            // Template-repo framing (D10.5): devs without the Vault container get Degraded, not red.
            return env.IsDevelopment()
                ? HealthCheckResult.Degraded("Vault unreachable in dev — spin up the Vault container")
                : HealthCheckResult.Unhealthy("Vault unreachable", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Vault health check failed unexpectedly");
            return HealthCheckResult.Unhealthy("Vault health check error", ex);
        }
    }

    // HttpRequestException covers TCP refusal; TaskCanceledException covers timeout.
    private static bool IsUnreachable(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;
}

/// <summary>Extension helper for registering <see cref="VaultHealthCheck"/>.</summary>
public static class VaultHealthCheckExtensions
{
    /// <summary>
    /// Adds <see cref="VaultHealthCheck"/> tagged as "ready" so it appears on /health/ready.
    /// Requires <see cref="IVaultClient"/> in DI — call AddVaultKeyResolver first.
    /// </summary>
    public static IHealthChecksBuilder AddVaultHealthCheck(this IHealthChecksBuilder builder) =>
        builder.AddCheck<VaultHealthCheck>(
            name: "vault",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["vault", "ready"]);
}
