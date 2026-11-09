using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Startup;

/// <summary>
/// Shared startup data-plane PII upgrade sequence, run by both the Api and AuthServer
/// composition roots. The column lists differ per host (each host owns a different set of
/// schemas), but the <b>step order is mandatory and identical</b>, so it lives in one place:
///
///   Step 1 — PlaintextToEncryptedUpgrader: plaintext → v{N}: for first-time PII columns.
///   Step 2 — EmailHashBackfillService: populate NormalizedEmailHash/EmailHash. Login is dead
///            until this completes; it requires Email/NormalizedEmail already encrypted (Step 1)
///            so EF's EncryptedStringConverter can decrypt them for hashing.
///   Step 3 — LegacyCipherUpgrader: v0 → v{N}: re-encryption for columns previously stored with
///            the no-prefix cipher format. Runs after Step 1 so all rows are encrypted.
///
/// All steps are idempotent (WHERE clauses skip already-upgraded rows) and fail-soft: a
/// missing encryption key (Vault unreachable in dev) logs a warning and skips, matching the
/// previous inline behaviour in each host.
/// </summary>
public static class StartupEncryptionUpgradeRunner
{
    public static async Task RunAsync(
        IServiceProvider services,
        string connectionString,
        IReadOnlyList<PlaintextToEncryptedUpgrader.ColumnTarget> plaintextColumns,
        IReadOnlyList<LegacyCipherUpgrader.ColumnTarget> legacyColumns,
        ILogger logger)
    {
        // === Step 1: plaintext → encrypted (MUST precede backfill + legacy-cipher) ===
        try
        {
            var keyResolver = services.GetRequiredService<IKeyResolver>();
            foreach (var column in plaintextColumns)
            {
                await PlaintextToEncryptedUpgrader.EncryptColumnAsync(
                    connectionString,
                    column,
                    keyResolver.GetKey,
                    keyResolver.CurrentVersion,
                    logger);
            }
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex,
                "Skipping plaintext→encrypted upgrade — encryption key unavailable (dev fallback without Vault)");
        }

        // === Step 2: backfill NormalizedEmailHash + EmailHash (login dead until populated) ===
        try
        {
            using var hashScope = services.CreateScope();
            var identityDbContext = hashScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var emailHashService = hashScope.ServiceProvider.GetRequiredService<EmailHashService>();

            await EmailHashBackfillService.BackfillAsync(identityDbContext, emailHashService, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "EmailHash backfill skipped — check IKeyResolver and IPepperResolver availability");
        }

        // === Step 3: legacy cipher upgrade v0 → v{N}: (after Step 1 encrypts all rows) ===
        if (legacyColumns.Count > 0)
        {
            try
            {
                var keyResolver = services.GetRequiredService<IKeyResolver>();
                var keyV1 = keyResolver.GetKey(1);
                foreach (var column in legacyColumns)
                {
                    await LegacyCipherUpgrader.UpgradeColumnAsync(
                        connectionString,
                        column,
                        keyV1,
                        keyResolver.GetKey,
                        keyResolver.CurrentVersion,
                        logger);
                }
            }
            catch (KeyNotFoundException ex)
            {
                logger.LogWarning(ex,
                    "Skipping legacy cipher upgrade — encryption key unavailable (dev fallback?)");
            }
        }
    }
}
