using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Hosting;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// IHostedService that backfills the [identity].[Tenants] table on startup.
/// Creates Tenant rows for every DISTINCT TenantId found in AspNetUsers OR OrgUnits
/// that doesn't already exist in the Tenants table.
///
/// Idempotent: only inserts missing rows; existing tenants are not modified.
/// Uses Dapper raw SQL — schema [identity] must be bracket-escaped (T-SQL reserved).
/// </summary>
public sealed class TenantBackfillSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<TenantBackfillSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDbConnection>();

        logger.LogInformation("[TenantBackfillSeeder] Starting backfill check...");

        try
        {
            var (insertedCount, skippedCount) = await BackfillTenantsAsync(db, logger, ct);

            if (skippedCount > 0)
            {
                logger.LogError(
                    "[TenantBackfillSeeder] Backfilled {Count} tenant(s), {Skipped} skipped due to errors — will retry on next startup",
                    insertedCount, skippedCount);
            }
            else if (insertedCount > 0)
            {
                logger.LogInformation(
                    "[TenantBackfillSeeder] Backfilled {Count} tenant(s)",
                    insertedCount);
            }
            else
            {
                logger.LogDebug("[TenantBackfillSeeder] No missing tenants — backfill not needed");
            }
        }
        catch (Exception ex)
        {
            // Outer guard: connectivity / SQL setup failure — don't block app startup.
            // Inner per-tenant failures are handled by BackfillTenantsAsync itself.
            logger.LogError(ex, "[TenantBackfillSeeder] Backfill failed — will retry on next startup");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Performs the idempotent backfill. Returns (inserted, skipped) counts.
    /// Per-tenant INSERT wrapped in try/catch so a single FK/PK violation does not
    /// abort the backfill for the rest. Survivors persist; failures retry next startup.
    /// </summary>
    public static async Task<(int Inserted, int Skipped)> BackfillTenantsAsync(
        IReadDbConnection db,
        ILogger logger,
        CancellationToken ct)
    {
        // Step 1: Find all TenantIds that need backfill
        // UNION of AspNetUsers.TenantId and OrgUnits.TenantId, EXCEPT existing Tenants.PublicId
        // Phase-04a: Tenant is T2 — Tenants.Id is bigint IDENTITY PK, Tenants.PublicId is Guid AK.
        // Cross-module FKs (AspNetUsers.TenantId, OrgUnits.TenantId) target Tenants.PublicId.
        // Note: [identity] and [organization] schemas must be bracket-escaped
        const string findMissingSql = """
            WITH AllTenantIds AS (
                SELECT DISTINCT TenantId
                FROM [identity].[AspNetUsers]
                WHERE TenantId IS NOT NULL
                UNION
                SELECT DISTINCT TenantId
                FROM [organization].[OrgUnits]
                WHERE TenantId IS NOT NULL
            )
            SELECT a.TenantId
            FROM AllTenantIds a
            WHERE NOT EXISTS (
                SELECT 1 FROM [identity].[Tenants] t WHERE t.PublicId = a.TenantId
            )
            """;

        var missingTenantIds = (await db.QueryAsync<Guid>(findMissingSql, new { }, ct)).ToList();

        if (missingTenantIds.Count == 0)
        {
            return (0, 0);
        }

        // Step 2: For each missing TenantId, get root OrgUnit name (if any)
        const string getRootNamesSql = """
            SELECT TenantId, Name
            FROM [organization].[OrgUnits]
            WHERE TenantId IN @TenantIds
              AND ParentId IS NULL
              AND Level = 1
            """;

        var rootNames = await db.QueryAsync<TenantRootRow>(
            getRootNamesSql,
            new { TenantIds = missingTenantIds },
            ct);

        var nameByTenantId = rootNames
            .GroupBy(r => r.TenantId)
            .ToDictionary(g => g.Key, g => g.First().Name);

        // Step 3: Insert missing tenants — per-tenant try/catch so a single bad row
        // (FK / unique violation, transient SQL error) does not abort the whole sweep.
        var insertedCount = 0;
        var skippedCount = 0;
        var now = DateTime.UtcNow;
        var systemActorId = Guid.Empty; // System-initiated backfill

        foreach (var tenantId in missingTenantIds)
        {
            ct.ThrowIfCancellationRequested();

            var shortId = tenantId.ToString("N")[..8].ToUpperInvariant();
            var name = nameByTenantId.TryGetValue(tenantId, out var rootName) && !string.IsNullOrWhiteSpace(rootName)
                ? rootName
                : $"Tenant {shortId}";
            var code = $"TENANT-{shortId}";

            // Column names must match AuditableEntity convention (ModifiedBy, NOT UpdatedBy).
            // RowVersion is auto-managed by SQL Server. IsDeleted defaults to 0; ClassificationLevel
            // defaults to 0 (Public) for backfilled rows. UpdatedAt/ModifiedBy NULL on initial seed.
            // Phase-04a: Tenants.Id is bigint IDENTITY (DB-assigned) — INSERT populates PublicId (Guid AK).
            const string insertSql = """
                INSERT INTO [identity].[Tenants]
                    (PublicId, Name, Code, ContactEmail, Status, CreatedAt, CreatedBy, UpdatedAt, ModifiedBy, IsDeleted, ClassificationLevel)
                VALUES
                    (@PublicId, @Name, @Code, NULL, @Status, @CreatedAt, @CreatedBy, NULL, NULL, 0, 0)
                """;

            try
            {
                await db.ExecuteAsync(insertSql, new
                {
                    PublicId = tenantId,
                    Name = name,
                    Code = code,
                    Status = (int)TenantStatus.Active,
                    CreatedAt = now,
                    CreatedBy = systemActorId
                }, ct);

                insertedCount++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                skippedCount++;
                logger.LogError(ex,
                    "[TenantBackfillSeeder] failed to insert tenant {TenantId} (code {Code}) — continuing",
                    tenantId, code);
            }
        }

        return (insertedCount, skippedCount);
    }

    // Class-based DTO for Dapper compatibility
    private sealed class TenantRootRow
    {
        public Guid TenantId { get; init; }
        public string Name { get; init; } = string.Empty;
    }
}
