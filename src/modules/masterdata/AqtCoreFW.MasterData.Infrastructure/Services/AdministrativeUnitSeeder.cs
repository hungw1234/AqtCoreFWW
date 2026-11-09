using System.Reflection;
using System.Text.Json;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.MasterData.Infrastructure.Services;

/// <summary>
/// Seeds the 2-level administrative-unit tree (hệ-34 reorg): Province (level 1) →
/// Ward/commune (level 2). There is no district level — the 2025 reorganization
/// collapsed districts. Data covers the three centrally-governed cities with full
/// ward detail (Hà Nội, TP. Hồ Chí Minh, Đà Nẵng); codes are generated
/// ("{provinceCode}{NNN}"), names carry full Vietnamese diacritics.
///
/// Idempotent: delta-inserts by Code (mirrors <see cref="ProvinceSeeder"/>), so a
/// re-run adds only missing codes and never duplicates. All seeded units are
/// active (no superseded/successor demo rows in this dataset).
/// </summary>
public static class AdministrativeUnitSeeder
{
    private const string Resource =
        "AqtCoreFW.MasterData.Infrastructure.Resources.admin-units-wards-34.json";

    public static async Task SeedAdministrativeUnitsAsync(
        MasterDataDbContext db, ILogger logger, CancellationToken ct)
    {
        var units = LoadEmbeddedJson<List<AdministrativeUnitSeedDto>>(Resource);
        if (units is null || units.Count == 0)
        {
            logger.LogWarning("AdministrativeUnitSeeder: admin-units-wards-34.json empty or missing");
            return;
        }

        var existingCodes = await db.AdministrativeUnits.Select(u => u.Code).ToListAsync(ct);
        var existing = existingCodes.ToHashSet();

        // Insert parents (level 1) before children so the tree is consistent within the batch.
        var newUnits = units
            .Where(u => !existing.Contains(u.Code))
            .OrderBy(u => u.Level)
            .ThenBy(u => u.SortOrder)
            .Select(u => AdministrativeUnit.Create(
                u.Code, u.NameVi, u.NameEn, u.Level, u.ParentCode, successorCode: null, isActive: true))
            .ToList();

        if (newUnits.Count > 0)
        {
            await db.AdministrativeUnits.AddRangeAsync(newUnits, ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "AdministrativeUnitSeeder: added {Count} new units (2-level hệ-34)", newUnits.Count);
        }
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

    private sealed record AdministrativeUnitSeedDto
    {
        public string Code { get; init; } = default!;
        public string NameVi { get; init; } = default!;
        public string NameEn { get; init; } = default!;
        public int Level { get; init; }
        public string? ParentCode { get; init; }
        public int SortOrder { get; init; }
    }
}
