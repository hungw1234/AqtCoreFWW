using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Enums;
using AqtCoreFW.Integration.Infrastructure.Persistence;
using AqtCoreFW.Integration.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Integration.Integration.Tests.Seeding;

/// <summary>
/// Integration coverage for <see cref="DemoIntegrationSeeder"/>. The seeder stages one Active Partner +
/// one Draft Contract per tenant (definitions only — no MessageLog). Real-SQL because the value under
/// test is the two-phase FK wiring (Contract.PartnerId = the DB-assigned partner.Id) and the
/// (TenantId, Code) idempotency probe, neither of which an in-memory provider would exercise faithfully.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DemoIntegrationSeederTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public DemoIntegrationSeederTests(WebAppFixture app) => _app = app;

    // ── T1: seeds one Active Partner + one Draft Contract per tenant, FK + diacritics correct ──

    [Fact]
    public async Task T1_Seeds_OnePartnerAndContract_PerTenant_WithValidFk()
    {
        await SeedTenantAsync(TenantSeedProfiles.BoTaiChinh);
        await SeedTenantAsync(TenantSeedProfiles.BoNoiVu);

        foreach (var profile in TenantSeedProfiles.All)
        {
            await using var ctx = NewFilterFreeCtx();

            var partner = await ctx.Partners
                .IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(p => p.TenantId == profile.TenantId && p.Code == DemoIntegrationSeeder.DemoPartnerCode);

            partner.Status.Should().Be(PartnerStatus.Active, $"{profile.Code} demo partner must be Active");
            partner.Name.Should().Be($"Đối tác tích hợp (demo) — {profile.Name}",
                "Vietnamese diacritics must round-trip (NVARCHAR)");

            var contract = await ctx.Contracts
                .IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(c => c.TenantId == profile.TenantId && c.PartnerId == partner.Id);

            contract.Status.Should().Be(ContractStatus.Draft, $"{profile.Code} demo contract must be Draft");
            contract.Title.Should().Be("Hợp đồng trao đổi dữ liệu (demo)", "diacritics round-trip");
            contract.PartnerId.Should().Be(partner.Id,
                "the two-phase save must wire the contract FK to the DB-assigned partner Id");
            contract.ExpiryDate.Should().BeNull("demo contract is open-ended");
        }
    }

    // ── T2: re-running the seeder is a no-op (idempotent per TenantId + Code) ──

    [Fact]
    public async Task T2_SecondRun_IsNoOp_NoDuplicatePartnersOrContracts()
    {
        var profile = TenantSeedProfiles.BoNoiVu;

        await SeedTenantAsync(profile);
        var (partnersBefore, contractsBefore) = await CountAsync(profile);

        // Faithful second run through a fresh context (mirrors a second startup).
        await SeedTenantAsync(profile);
        var (partnersAfter, contractsAfter) = await CountAsync(profile);

        partnersBefore.Should().Be(1);
        contractsBefore.Should().Be(1);
        partnersAfter.Should().Be(1, "idempotency: a second seed run must not create a duplicate partner");
        contractsAfter.Should().Be(1, "no duplicate contract on re-run");
    }

    // ── T3: the (TenantId, Code) key is tenant-scoped — both tenants share a Code as distinct rows ──

    [Fact]
    public async Task T3_SameCode_AcrossTenants_AreDistinctRows()
    {
        await SeedTenantAsync(TenantSeedProfiles.BoTaiChinh);
        await SeedTenantAsync(TenantSeedProfiles.BoNoiVu);

        await using var ctx = NewFilterFreeCtx();
        var partners = await ctx.Partners
            .IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Code == DemoIntegrationSeeder.DemoPartnerCode
                        && (p.TenantId == TenantSeedProfiles.BoTaiChinh.TenantId
                            || p.TenantId == TenantSeedProfiles.BoNoiVu.TenantId))
            .ToListAsync();

        partners.Should().HaveCount(2, "the unique (TenantId, Code) index is tenant-scoped, not global");
        partners.Select(p => p.TenantId).Should().OnlyHaveUniqueItems("one partner per tenant");
        partners.Select(p => p.Id).Should().OnlyHaveUniqueItems("distinct IDENTITY rows");
        partners.Select(p => p.PublicId).Should().OnlyHaveUniqueItems("distinct external AKs");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Drives the seeder's static per-tenant path through a fresh filter-free context (mirrors startup).</summary>
    private async Task SeedTenantAsync(TenantSeedProfile profile)
    {
        using var scope = _app.Services.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        await using var ctx = NewFilterFreeCtx();
        await DemoIntegrationSeeder.SeedTenantIntegrationAsync(
            ctx, profile, gen, NullLogger.Instance, CancellationToken.None);
    }

    private async Task<(int Partners, int Contracts)> CountAsync(TenantSeedProfile profile)
    {
        await using var ctx = NewFilterFreeCtx();
        var partners = await ctx.Partners
            .IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.TenantId == profile.TenantId && p.Code == DemoIntegrationSeeder.DemoPartnerCode)
            .Select(p => p.Id)
            .ToListAsync();

        var contracts = await ctx.Contracts
            .IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == profile.TenantId && partners.Contains(c.PartnerId))
            .CountAsync();

        return (partners.Count, contracts);
    }

    /// <summary>Filter-free IntegrationDbContext (system seed context) — reads/writes span tenants like the seeder.</summary>
    private IntegrationDbContext NewFilterFreeCtx() =>
        new(_app.Services.GetRequiredService<DbContextOptions<IntegrationDbContext>>(),
            SystemSeederTenantContext.Instance);
}
