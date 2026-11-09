using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Boot-time hosted service — emits startup warnings about digital signing configuration health.
///
/// M-1 handover checks:
///   1. VnCaRootStore is empty (PRODUCTION_SEED_DEFERRED) — chain validation will reject all certs.
///   2. One or more tenants have IsEnabled=true but the root store is empty — those tenants
///      will receive chain validation failures at runtime.
///
/// Does NOT block startup — logs at Warning only. Ops teams resolve via NEAC root bundle acquisition.
/// Runs once on startup and exits (not a recurring background task).
/// </summary>
public sealed class SigningHealthCheckService(
    IServiceProvider serviceProvider,
    VnCaRootStore rootStore,
    ILogger<SigningHealthCheckService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var rootCount = rootStore.TrustedRootCount;

        if (rootCount == 0)
        {
            logger.LogWarning(
                "SIGNING-HEALTH: VnCaRootStore contains 0 trusted roots. " +
                "Chain validation will reject ALL certificate chains. " +
                "Acquire NEAC root bundle and seed VnCaRootStore before enabling digital signature. " +
                "(PRODUCTION_SEED_DEFERRED — see compliance dossier)");

            // Check for tenants that are already enabled — extra-severity warning
            try
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

                var enabledCount = await db.Set<AqtCoreFW.Identity.Domain.Entities.TenantSignatureConfig>()
                    .Where(c => c.IsEnabled)
                    .CountAsync(cancellationToken);

                if (enabledCount > 0)
                {
                    logger.LogWarning(
                        "SIGNING-HEALTH: {Count} tenant(s) have IsEnabled=true but VnCaRootStore " +
                        "has no trusted roots — verify requests will fail chain validation. " +
                        "Disable signing or seed root certificates immediately.",
                        enabledCount);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal — DB may not be migrated yet at startup
                logger.LogDebug(ex,
                    "SIGNING-HEALTH: Could not query TenantSignatureConfig during startup check.");
            }
        }
        else
        {
            logger.LogInformation(
                "SIGNING-HEALTH: VnCaRootStore has {Count} trusted root(s) — chain validation operational.",
                rootCount);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
