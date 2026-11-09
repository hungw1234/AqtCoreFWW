using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// IHostedService that proactively seeds the canonical <c>[identity].[Tenants]</c> rows for every
/// <see cref="TenantSeedProfiles"/> entry (Bộ Tài chính + Bộ Nội Vụ), with diacritic-correct names
/// and stable codes.
///
/// <para>Why raw SQL (not <c>Tenant.Create</c>): the domain factory generates a fresh v7 PublicId,
/// but seeding requires the PublicId to equal the profile's deterministic TenantId — that GUID is
/// the cross-module FK target (AspNetUsers.TenantId / OrgUnits.TenantId). We therefore insert the
/// row directly, mirroring <see cref="TenantBackfillSeeder"/>'s proven INSERT shape.</para>
///
/// <para>Idempotent: only inserts profiles whose PublicId is not already present. This is also what
/// makes it safe alongside <see cref="TenantBackfillSeeder"/> — whichever runs first, the other
/// finds the row by PublicId and skips. The API host registers this seeder BEFORE the backfill
/// seeder (explicit-order block in Program.cs, pinned by SeederStartupOrderGuardTests) so the
/// canonical row (proper code + diacritics) wins over the backfill's derived "TENANT-xxxx"
/// fallback.</para>
///
/// <para>Default OFF (see <see cref="TenantSeederOptions"/>): only the reseed flow enables it, so
/// normal dev startup behaviour is unchanged.</para>
/// </summary>
public sealed class TenantSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<TenantSeederOptions> options,
    ILogger<TenantSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly TenantSeederOptions _opts = options.Value;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opts.Enabled)
        {
            logger.LogDebug("[TenantSeeder] Disabled — skipping proactive tenant seed");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDbConnection>();

        logger.LogInformation("[TenantSeeder] Seeding {Count} canonical tenant(s)...", TenantSeedProfiles.All.Count);

        try
        {
            var (inserted, skipped) = await SeedTenantsAsync(db, TenantSeedProfiles.All, logger, ct);
            logger.LogInformation(
                "[TenantSeeder] complete — {Inserted} inserted, {Skipped} already present",
                inserted, skipped);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Don't block app startup; TenantBackfillSeeder remains the reactive fallback.
            logger.LogError(ex, "[TenantSeeder] Seed failed — backfill will reconcile on next startup");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Inserts a Tenant row for each profile whose PublicId is not yet present. Returns
    /// (inserted, skipped). Per-row try/catch so a single PK/unique violation does not abort the rest.
    /// </summary>
    public static async Task<(int Inserted, int Skipped)> SeedTenantsAsync(
        IReadDbConnection db,
        IReadOnlyList<TenantSeedProfile> profiles,
        ILogger logger,
        CancellationToken ct)
    {
        // Pre-load existing PublicIds so we insert only what's missing (idempotent + backfill-safe).
        const string existingSql = "SELECT PublicId FROM [identity].[Tenants]";
        var existing = (await db.QueryAsync<Guid>(existingSql, new { }, ct)).ToHashSet();

        // Column set + conventions mirror TenantBackfillSeeder: Tenants.Id is bigint IDENTITY (DB-assigned),
        // PublicId is the Guid AK. RowVersion auto-managed. ModifiedBy (not UpdatedBy) per AuditableEntity.
        const string insertSql = """
            INSERT INTO [identity].[Tenants]
                (PublicId, Name, Code, ContactEmail, Status, CreatedAt, CreatedBy, UpdatedAt, ModifiedBy, IsDeleted, ClassificationLevel)
            VALUES
                (@PublicId, @Name, @Code, @ContactEmail, @Status, @CreatedAt, @CreatedBy, NULL, NULL, 0, 0)
            """;

        var inserted = 0;
        var skipped = 0;
        var now = DateTime.UtcNow;
        var systemActorId = Guid.Empty; // System-initiated seed

        foreach (var profile in profiles)
        {
            ct.ThrowIfCancellationRequested();

            if (existing.Contains(profile.TenantId))
            {
                skipped++;
                continue;
            }

            try
            {
                await db.ExecuteAsync(insertSql, new
                {
                    PublicId = profile.TenantId,
                    Name = profile.Name,
                    Code = profile.Code,
                    ContactEmail = profile.ContactEmail,
                    Status = (int)TenantStatus.Active,
                    CreatedAt = now,
                    CreatedBy = systemActorId,
                }, ct);

                inserted++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                skipped++;
                logger.LogError(ex,
                    "[TenantSeeder] failed to insert tenant {Code} ({TenantId}) — continuing",
                    profile.Code, profile.TenantId);
            }
        }

        return (inserted, skipped);
    }
}
