using System.Reflection;
using System.Text.Json;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.MasterData.Infrastructure.Services;

/// <summary>
/// Seeds 34 provinces (post QD 19/2025/QD-TTg) and 63->34 legacy mappings.
/// Idempotent: checks existing count before seeding.
/// </summary>
public static class ProvinceSeeder
{
    private const string Provinces34Resource = "AqtCoreFW.MasterData.Infrastructure.Resources.provinces-34-official.json";
    private const string LegacyMappingResource = "AqtCoreFW.MasterData.Infrastructure.Resources.province-legacy-mapping.json";
    private static readonly DateTimeOffset MergedAt = new(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(7));

    public static async Task SeedProvincesAsync(MasterDataDbContext db, ILogger logger, CancellationToken ct)
    {
        var existingCodes = await db.Provinces.Select(p => p.Code).ToListAsync(ct);

        // Load 34 provinces from embedded JSON
        var provinces34 = LoadEmbeddedJson<List<ProvinceDto>>(Provinces34Resource);
        if (provinces34 is null || provinces34.Count == 0)
        {
            logger.LogWarning("ProvinceSeeder: provinces-34-official.json empty or missing");
            return;
        }

        // Delta insert: only add provinces that don't exist
        var newProvinces = provinces34
            .Where(p => !existingCodes.Contains(p.Code))
            .Select(p => Province.Create(p.Code, p.NameVi, p.NameEn, p.SortOrder))
            .ToList();

        if (newProvinces.Count > 0)
        {
            await db.Provinces.AddRangeAsync(newProvinces, ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("ProvinceSeeder: added {Count} new provinces (34-system)", newProvinces.Count);
        }

        // NOTE: Legacy mock deactivation moved to one-shot migration (20260422XXXXXX_DeactivateLegacyMockProvinces)
        // to avoid silently deactivating ops-inserted provinces on every boot.
    }

    public static async Task SeedLegacyMappingsAsync(MasterDataDbContext db, ILogger logger, CancellationToken ct)
    {
        // Ensure table exists (EnsureCreatedAsync only creates if DB is empty)
        await EnsureLegacyMappingTableExistsAsync(db, logger, ct);

        if (await db.ProvinceLegacyMappings.AnyAsync(ct))
        {
            logger.LogDebug("ProvinceSeeder: legacy mappings already exist, skipping");
            return;
        }

        var mappings = LoadEmbeddedJson<List<LegacyMappingDto>>(LegacyMappingResource);
        if (mappings is null || mappings.Count == 0)
        {
            logger.LogWarning("ProvinceSeeder: province-legacy-mapping.json empty or missing");
            return;
        }

        var entities = mappings
            .Select(m => ProvinceLegacyMapping.Create(m.OldCode, m.NewCode, MergedAt, m.Notes))
            .ToList();

        await db.ProvinceLegacyMappings.AddRangeAsync(entities, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("ProvinceSeeder: added {Count} legacy mappings (63->34)", entities.Count);
    }

    private static T? LoadEmbeddedJson<T>(string resourceName) where T : class
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    /// <summary>Creates ProvinceLegacyMappings table if not exists (for existing DBs created via EnsureCreatedAsync).</summary>
    private static async Task EnsureLegacyMappingTableExistsAsync(MasterDataDbContext db, ILogger logger, CancellationToken ct)
    {
        const string checkSql = """
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'masterdata' AND TABLE_NAME = 'ProvinceLegacyMappings')
            BEGIN
                CREATE TABLE [masterdata].[ProvinceLegacyMappings] (
                    [Id] uniqueidentifier NOT NULL,
                    [OldCode] nvarchar(10) NOT NULL,
                    [NewCode] nvarchar(10) NOT NULL,
                    [MergedAt] datetimeoffset NOT NULL,
                    [Notes] nvarchar(500) NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [UpdatedAt] datetimeoffset NULL,
                    [IsDeleted] bit NOT NULL DEFAULT 0,
                    CONSTRAINT [PK_ProvinceLegacyMappings] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_ProvinceLegacyMappings_OldCode] ON [masterdata].[ProvinceLegacyMappings] ([OldCode]);
                CREATE INDEX [IX_ProvinceLegacyMappings_NewCode] ON [masterdata].[ProvinceLegacyMappings] ([NewCode]);
            END
            """;
        await db.Database.ExecuteSqlRawAsync(checkSql, ct);
        logger.LogDebug("ProvinceSeeder: ensured ProvinceLegacyMappings table exists");
    }

    private sealed record ProvinceDto(string Code, string NameVi, string NameEn, int SortOrder);
    private sealed record LegacyMappingDto(string OldCode, string NewCode, string? Notes);
}
