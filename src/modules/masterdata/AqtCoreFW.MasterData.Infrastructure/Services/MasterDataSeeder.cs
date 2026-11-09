using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.MasterData.Infrastructure.Services;

/// <summary>
/// Seeds reference data at startup (idempotent — COUNT check, synchronous on StartAsync so app waits).
/// Also loads Province/District/Ward into IMemoryCache NeverRemove after seeding.
/// </summary>
public class MasterDataSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<MasterDataSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
        var adminUnitService = scope.ServiceProvider.GetRequiredService<AdministrativeUnitService>();
        var guidGen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        await db.Database.EnsureCreatedAsync(ct);

        // Province 34-system seeding (QD 19/2025/QD-TTg) + legacy 63->34 mappings
        await ProvinceSeeder.SeedProvincesAsync(db, logger, ct);
        await ProvinceSeeder.SeedLegacyMappingsAsync(db, logger, ct);
        await SeedDistrictsAndWardsAsync(db, ct);

        await AdministrativeUnitSeeder.SeedAdministrativeUnitsAsync(db, logger, ct);
        await SeedJobTitlesAsync(db, ct);

        // Phase-00: workflow taxonomy baseline vocabulary (R2 strict per-Code idempotency)
        await WorkflowTaxonomySeeder.SeedStatesAsync(db, guidGen, WorkflowTaxonomySeeder.BaselineStates, ct);
        await WorkflowTaxonomySeeder.SeedActionsAsync(db, guidGen, WorkflowTaxonomySeeder.BaselineActions, ct);

        await adminUnitService.BuildCacheAsync(ct);
        await LoadHotDataCacheAsync(scope.ServiceProvider, db, ct);

        logger.LogInformation("MasterData seeding and cache loading complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task SeedDistrictsAndWardsAsync(MasterDataDbContext db, CancellationToken ct)
    {
        // Seed sample districts if none exist (Hanoi only for demo)
        if (!await db.Districts.AnyAsync(ct))
        {
            var districts = new[]
            {
                District.Create("001", "01", "Quận Ba Đình", "Ba Dinh District", 1),
                District.Create("002", "01", "Quận Hoàn Kiếm", "Hoan Kiem District", 2),
                District.Create("003", "01", "Quận Tây Hồ", "Tay Ho District", 3),
            };
            await db.Districts.AddRangeAsync(districts, ct);
        }

        // Seed sample wards if none exist
        if (!await db.Wards.AnyAsync(ct))
        {
            var wards = new[]
            {
                Ward.Create("00001", "001", "Phường Phúc Xá", "Phuc Xa Ward", 1),
                Ward.Create("00004", "001", "Phường Trúc Bạch", "Truc Bach Ward", 2),
                Ward.Create("00006", "001", "Phường Vĩnh Phúc", "Vinh Phuc Ward", 3),
            };
            await db.Wards.AddRangeAsync(wards, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    // CaseTypes seeding removed: the Cases module was retired. The GetCaseTypes
    // endpoint stays (returns empty) for API back-compat; nothing seeds the table.

    private static async Task SeedJobTitlesAsync(MasterDataDbContext db, CancellationToken ct)
    {
        // Government job-title hierarchy, shared across ministries (TenantId null —
        // both Bộ Tài chính and Bộ Nội Vụ use the same civil-service titles).
        // Delta-insert by code so adding titles later is idempotent.
        var titles = new[]
        {
            JobTitle.Create("BO_TRUONG",          "Bộ trưởng",              "Minister", null, 1),
            JobTitle.Create("THU_TRUONG",         "Thứ trưởng",             "Vice Minister", null, 2),
            JobTitle.Create("TONG_CUC_TRUONG",    "Tổng cục trưởng",        "Director General", null, 3),
            JobTitle.Create("PHO_TONG_CUC_TRUONG","Phó Tổng cục trưởng",    "Deputy Director General", null, 4),
            JobTitle.Create("CUC_TRUONG",         "Cục trưởng",             "Department Director", null, 5),
            JobTitle.Create("PHO_CUC_TRUONG",     "Phó Cục trưởng",         "Deputy Department Director", null, 6),
            JobTitle.Create("VU_TRUONG",          "Vụ trưởng",              "Director of Affairs", null, 7),
            JobTitle.Create("PHO_VU_TRUONG",      "Phó Vụ trưởng",          "Deputy Director of Affairs", null, 8),
            JobTitle.Create("CHI_CUC_TRUONG",     "Chi cục trưởng",         "Sub-Department Head", null, 9),
            JobTitle.Create("PHO_CHI_CUC_TRUONG", "Phó Chi cục trưởng",     "Deputy Sub-Department Head", null, 10),
            JobTitle.Create("TRUONG_PHONG",       "Trưởng phòng",           "Division Head", null, 11),
            JobTitle.Create("PHO_PHONG",          "Phó Trưởng phòng",       "Deputy Division Head", null, 12),
            JobTitle.Create("CHUYEN_VIEN_CC",     "Chuyên viên cao cấp",    "Senior Principal Officer", null, 13),
            JobTitle.Create("CHUYEN_VIEN_CHINH",  "Chuyên viên chính",      "Principal Officer", null, 14),
            JobTitle.Create("CHUYEN_VIEN",        "Chuyên viên",            "Officer", null, 15),
            JobTitle.Create("CAN_SU",             "Cán sự",                 "Junior Officer", null, 16),
            JobTitle.Create("VAN_THU",            "Văn thư",                "Clerk", null, 17),
        };

        var existing = (await db.JobTitles.Select(j => j.Code).ToListAsync(ct)).ToHashSet();
        var newTitles = titles.Where(t => !existing.Contains(t.Code)).ToList();
        if (newTitles.Count > 0)
        {
            await db.JobTitles.AddRangeAsync(newTitles, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task LoadHotDataCacheAsync(
        IServiceProvider sp, MasterDataDbContext db, CancellationToken ct)
    {
        var cache = sp.GetRequiredService<IMemoryCache>();
        var opts = new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.NeverRemove,
            Size = 1
        };

        var provinces = await db.Provinces.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);
        var districts = await db.Districts.AsNoTracking().OrderBy(d => d.SortOrder).ToListAsync(ct);
        var wards = await db.Wards.AsNoTracking().OrderBy(w => w.SortOrder).ToListAsync(ct);

        cache.Set("masterdata:provinces", provinces, opts);
        cache.Set("masterdata:districts", districts, opts);
        cache.Set("masterdata:wards", wards, opts);
    }
}
