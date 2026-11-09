using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Infrastructure.Services;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.MasterData;

/// <summary>
/// Real-SQL coverage for <see cref="AdministrativeUnitSeeder"/>: the 2-level hệ-34
/// geo tree (34 provinces at level 1 + their wards at level 2, total 3317 wards).
/// Asserts canonical per-province counts (HN 126 / HCM 164 / ĐN 94 + sampled
/// non-city provinces), parent wiring, generated-code format, diacritic fidelity,
/// and delta-insert idempotency.
///
/// HCM is 164 (not the official 168): the source double-lists 4 "Dầu Tiếng" units
/// under both phường and xã with an explicit classification caveat, so they dedup
/// to 4 distinct units. The seeder ships the 164 unique units.
/// </summary>
[Collection("Integration")]
public class AdministrativeUnitSeederTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string HanoiCode = "01";
    private const string HcmCode = "79";
    private const string DaNangCode = "48";
    private const string CaoBangCode = "04";   // sampled non-city province (hệ-34)
    private const string NgheAnCode = "40";     // sampled large non-city province

    private async Task<List<AdministrativeUnit>> SeedAndReadAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
        // Idempotent delta-insert — guarantees the dataset is present regardless of
        // whether the startup hosted seeder already ran in this fixture.
        await AdministrativeUnitSeeder.SeedAdministrativeUnitsAsync(ctx, NullLogger.Instance, CancellationToken.None);
        return await ctx.AdministrativeUnits.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task Seeds_TwoLevelTree_WithCanonicalCounts()
    {
        var units = await SeedAndReadAsync();

        var provinces = units.Where(u => u.Level == 1).ToList();
        provinces.Should().HaveCount(34, "all 34 hệ-34 provinces are seeded at level 1");
        provinces.Select(c => c.Code).Should().Contain([HanoiCode, HcmCode, DaNangCode, CaoBangCode, NgheAnCode]);
        provinces.Should().OnlyContain(c => c.ParentCode == null, "level-1 provinces have no parent");

        // Per-province ward counts straight from each province's NQ-stated total (count-gate verified).
        units.Count(u => u.Level == 2 && u.ParentCode == HanoiCode).Should().Be(126, "Hà Nội 51 phường + 75 xã");
        units.Count(u => u.Level == 2 && u.ParentCode == HcmCode).Should().Be(164, "TP.HCM 168 official − 4 deduped Dầu Tiếng");
        units.Count(u => u.Level == 2 && u.ParentCode == DaNangCode).Should().Be(94, "Đà Nẵng 23 phường + 70 xã + Hoàng Sa");
        units.Count(u => u.Level == 2 && u.ParentCode == CaoBangCode).Should().Be(56, "Cao Bằng 53 xã + 3 phường (NQ 1657)");
        units.Count(u => u.Level == 2 && u.ParentCode == NgheAnCode).Should().Be(130, "Nghệ An 119 xã + 11 phường (NQ 1678)");
        units.Count(u => u.Level == 2).Should().Be(3317, "total wards across all 34 provinces");

        // No third level — the 2025 reorg collapsed districts.
        units.Should().NotContain(u => u.Level >= 3, "the model is 2-level (Province → Ward), no district");
    }

    [Fact]
    public async Task Seeds_GeneratedCodes_ParentedAndUnique()
    {
        var units = await SeedAndReadAsync();
        var wards = units.Where(u => u.Level == 2).ToList();
        var provinceCodes = units.Where(u => u.Level == 1).Select(u => u.Code).ToHashSet();

        wards.Should().OnlyContain(w => provinceCodes.Contains(w.ParentCode!),
            "every ward parents to one of the seeded provinces");
        wards.Should().OnlyContain(w => w.Code.StartsWith(w.ParentCode!) && w.Code.Length == 5,
            "ward code = {provinceCode}{NNN}");
        wards.Should().OnlyContain(w => w.IsActive, "all seeded wards are active (no superseded demo rows)");

        var allCodes = units.Select(u => u.Code).ToList();
        allCodes.Should().OnlyHaveUniqueItems("codes are the primary key — no collisions");
    }

    [Fact]
    public async Task Seeds_PreservesVietnameseDiacritics()
    {
        var units = await SeedAndReadAsync();
        var byName = units.Select(u => u.NameVi).ToHashSet();

        // Spot-check exact diacritics straight from the source list (no transcription drift).
        byName.Should().Contain("Hoàn Kiếm");
        byName.Should().Contain("Văn Miếu - Quốc Tử Giám");
        byName.Should().Contain("Đoài Phương");
        byName.Should().Contain("La Êê");          // Đà Nẵng commune with stacked diacritics
        byName.Should().Contain("Hoàng Sa");        // Đà Nẵng đặc khu
        byName.Should().Contain("Côn Đảo");         // HCM đặc khu
        byName.Should().Contain("Bế Văn Đàn");      // Cao Bằng commune (non-city province, stacked diacritics)

        // nameEn is a deterministic ASCII fold (Đ→D, diacritics stripped) — holds across all provinces.
        units.Single(u => u.Code == "01001").NameEn.Should().Be("Hoan Kiem");
        units.Single(u => u.Code == "04039").NameEn.Should().Be("Be Van Dan");
    }

    [Fact]
    public async Task Seeder_IsIdempotent()
    {
        var first = await SeedAndReadAsync();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
        await AdministrativeUnitSeeder.SeedAdministrativeUnitsAsync(ctx, NullLogger.Instance, CancellationToken.None);
        var second = await ctx.AdministrativeUnits.AsNoTracking().CountAsync();

        second.Should().Be(first.Count, "re-running the delta-insert adds nothing");
    }
}
