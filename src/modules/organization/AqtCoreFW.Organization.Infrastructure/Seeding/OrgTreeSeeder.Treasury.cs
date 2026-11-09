using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Organization.Infrastructure.Seeding;

/// <summary>
/// BTC-specific L4 step (ported from the legacy treasury partial): generate KBNN Phòng Giao dịch
/// placeholder nodes from masterdata.Wards. Plan-gated (only tenants whose plan sets
/// GenerateTreasuryWardPlaceholders run it). Code convention BTC.KBNN.PLACEHOLDER.{WardCode} allows
/// bulk-replace when the official KBNN Giao dịch Phòng list lands.
///
/// Cross-module read: queries masterdata.Wards via Dapper on the same DB to avoid compile-time
/// coupling to MasterData.Infrastructure. Parent assignment is intentionally degenerate (all wards
/// under the first KBNN khu vực) until a ProvinceToKbnnRegionMapping exists.
/// </summary>
public sealed partial class OrgTreeSeeder
{
    private sealed record WardDto(string Code, string NameVi);

    private static async Task SeedTreasuryWardPlaceholdersAsync(
        OrgDbContext ctx,
        Guid tenantId,
        IGuidGenerator gen,
        ILogger logger,
        CancellationToken ct)
    {
        logger.LogInformation("[OrgTreeSeeder] Starting L4 KBNN placeholder seed");

        var connStr = ctx.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connStr))
        {
            logger.LogWarning("[OrgTreeSeeder] L4 KBNN placeholder: no connection string — skip");
            return;
        }

        List<WardDto> wards;
        try
        {
            await using var conn = new SqlConnection(connStr);
            const string sql = """
                SELECT Code, NameVi
                FROM masterdata.Wards
                WHERE IsActive = 1
                """;
            var cmd = new CommandDefinition(sql, cancellationToken: ct);
            wards = (await conn.QueryAsync<WardDto>(cmd)).ToList();
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "[OrgTreeSeeder] L4 KBNN placeholder: failed to query masterdata.Wards — skip");
            return;
        }

        if (wards.Count == 0)
        {
            logger.LogWarning("[OrgTreeSeeder] L4 KBNN placeholder: no wards in MasterData — skip");
            return;
        }

        // Degenerate parent: first KBNN khu vực. Future: ProvinceToKbnnRegionMapping for accuracy.
        var kbnnL3 = await OrgSeedHelpers.BuildCodeToIdMapAsync(ctx, tenantId, level: 3, ct);
        var kbnnParent = kbnnL3.FirstOrDefault(kvp => kvp.Key.StartsWith("BTC.KBNN."));

        if (kbnnParent.Key is null)
        {
            logger.LogWarning("[OrgTreeSeeder] L4 KBNN placeholder: no KBNN khu vực parent — skip");
            return;
        }

        logger.LogWarning(
            "[OrgTreeSeeder] L4 KBNN placeholder: degenerate parent assignment — all wards under {ParentCode}. " +
            "Implement ProvinceToKbnnRegionMapping before real ward data ships.",
            kbnnParent.Key);

        var seedCodes = wards.Select(w => $"BTC.KBNN.PLACEHOLDER.{w.Code}").ToList();

        var existingCodes = await ctx.OrgUnits
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId && seedCodes.Contains(o.Code))
            .Select(o => o.Code)
            .ToHashSetAsync(ct);

        if (existingCodes.Count == seedCodes.Count)
        {
            logger.LogInformation(
                "[OrgTreeSeeder] L4 KBNN placeholder already fully seeded ({Count}) — skip",
                seedCodes.Count);
            return;
        }

        var entities = wards
            .Where(w => !existingCodes.Contains($"BTC.KBNN.PLACEHOLDER.{w.Code}"))
            .Select(w => OrgUnit.Create(
                gen: gen,
                name: $"KBNN Phòng Giao dịch {w.NameVi}",
                nameEn: $"KBNN Treasury Branch {w.NameVi}",
                code: $"BTC.KBNN.PLACEHOLDER.{w.Code}",
                tenantId: tenantId,
                parentId: kbnnParent.Value,
                level: 4))
            .ToList();

        await OrgSeedHelpers.BatchInsertAsync(ctx, entities, batchSize: 500, ct);
        ctx.ChangeTracker.Clear();

        logger.LogInformation(
            "[OrgTreeSeeder] L4 KBNN placeholder seeded: {NewCount} new (out of {Total} wards)",
            entities.Count, wards.Count);
    }
}
