using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Organization.Infrastructure.Seeding;

/// <summary>
/// Helper methods for OrgUnit seeding — idempotency checks, batch inserts, parent resolution.
/// All queries use IgnoreQueryFilters() because seeder runs pre-JWT (no ITenantContext).
/// </summary>
public static class OrgSeedHelpers
{
    /// <summary>
    /// Check if an OrgUnit with given code exists for the tenant.
    /// Uses IgnoreQueryFilters() to bypass tenant filter (seeder has no JWT context).
    /// </summary>
    public static async Task<bool> NodeExistsAsync(
        OrgDbContext ctx,
        Guid tenantId,
        string code,
        CancellationToken ct = default)
    {
        return await ctx.OrgUnits
            .IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.Code == code, ct);
    }

    /// <summary>
    /// Batch insert entities with change tracker optimization.
    /// Disables AutoDetectChanges during bulk add, then saves once.
    /// </summary>
    public static async Task BatchInsertAsync<T>(
        OrgDbContext ctx,
        IEnumerable<T> entities,
        int batchSize,
        CancellationToken ct = default) where T : class
    {
        var list = entities.ToList();
        if (list.Count == 0) return;

        var originalAutoDetect = ctx.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            ctx.ChangeTracker.AutoDetectChangesEnabled = false;

            foreach (var batch in list.Chunk(batchSize))
            {
                ctx.Set<T>().AddRange(batch);
                await ctx.SaveChangesAsync(ct);
            }
        }
        finally
        {
            ctx.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetect;
        }
    }

    /// <summary>
    /// Build a dictionary mapping Code → Id (long surrogate key) for OrgUnits at a given level.
    /// Used for parent resolution when seeding child levels.
    /// OrgUnit is T2: internal Id is long; seeders pass it as parentId to OrgUnit.Create.
    /// Uses IgnoreQueryFilters() to bypass tenant filter.
    /// </summary>
    public static async Task<Dictionary<string, long>> BuildCodeToIdMapAsync(
        OrgDbContext ctx,
        Guid tenantId,
        int level,
        CancellationToken ct = default)
    {
        return await ctx.OrgUnits
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.Level == level)
            .ToDictionaryAsync(x => x.Code, x => x.Id, ct);
    }

    /// <summary>
    /// Get a single OrgUnit by code for the tenant.
    /// Uses IgnoreQueryFilters() to bypass tenant filter.
    /// </summary>
    public static async Task<OrgUnit?> GetByCodeAsync(
        OrgDbContext ctx,
        Guid tenantId,
        string code,
        CancellationToken ct = default)
    {
        return await ctx.OrgUnits
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == code, ct);
    }
}
