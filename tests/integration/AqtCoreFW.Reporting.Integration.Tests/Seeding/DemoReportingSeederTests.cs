using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.Reporting.Infrastructure.Persistence;
using AqtCoreFW.Reporting.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Reporting.Integration.Tests.Seeding;

/// <summary>
/// Integration coverage for <see cref="DemoReportingSeeder"/>. The seeder stages, per tenant, one
/// report definition (string-slug tenant) + one widget (Guid tenant) + one dashboard whose grid
/// references the seeded widget by id, plus a single GLOBAL query-catalog entry (no tenant key). Real-SQL
/// because the values under test are (a) the two-phase save where the dashboard grid JSON must carry the
/// DB-assigned widget.Id, (b) the three different tenant-scope shapes, and (c) the idempotency probes —
/// none of which an in-memory provider exercises faithfully.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DemoReportingSeederTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public DemoReportingSeederTests(WebAppFixture app) => _app = app;

    // ── T1: per-tenant report def + widget + dashboard (grid → widget.Id) + 1 global catalog entry ──

    [Fact]
    public async Task T1_Seeds_ReportDefinitionWidgetDashboard_PerTenant_PlusGlobalCatalog()
    {
        await SeedGlobalCatalogAsync();
        await SeedTenantAsync(TenantSeedProfiles.BoTaiChinh);
        await SeedTenantAsync(TenantSeedProfiles.BoNoiVu);

        foreach (var profile in TenantSeedProfiles.All)
        {
            await using var ctx = NewFilterFreeCtx();
            var tenantSlug = profile.TenantId.ToString();

            var report = await ctx.ReportDefinitions
                .IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(r => r.TenantId == tenantSlug && r.Name == DemoReportingSeeder.DemoReportName);

            report.IsActive.Should().BeTrue("seeded report definitions are active");
            report.NameVi.Should().Be("Báo cáo tổng hợp hồ sơ (demo)", "Vietnamese diacritics must round-trip");
            report.OutputFormat.Should().Be(OutputFormat.Both);
            report.PublicId.Should().NotBe(Guid.Empty, "the v7 external AK is assigned at create");

            var widget = await ctx.ReportWidgets
                .IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(w => w.TenantId == profile.TenantId && w.Name == DemoReportingSeeder.DemoWidgetName);

            widget.Type.Should().Be(WidgetType.KPI);

            var dashboard = await ctx.DashboardLayouts
                .IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(d => d.TenantId == profile.TenantId
                                  && d.Name == $"Bảng điều khiển tổng quan (demo) — {profile.Name}");

            // Two-phase proof: the grid JSON references the DB-assigned widget.Id (known only after phase 1).
            dashboard.WidgetPositions.Should().Contain($"\"widgetId\":\"{widget.Id}\"",
                "the dashboard grid must reference the seeded widget by its assigned id");
        }

        // The catalog entry is global — exactly one row regardless of tenant count.
        await using var globalCtx = NewFilterFreeCtx();
        var catalog = await globalCtx.QueryCatalogEntries
            .IgnoreQueryFilters().AsNoTracking()
            .Where(q => q.Code == DemoReportingSeeder.DemoQueryCatalogCode)
            .ToListAsync();

        catalog.Should().HaveCount(1, "the query catalog has no tenant key — seeded once");
        catalog[0].DescriptionVi.Should().Be("Truy vấn đếm tổng hợp hồ sơ (demo).", "diacritics round-trip");
    }

    // ── T2: re-running the seeder is a no-op (idempotent per entity) ──

    [Fact]
    public async Task T2_SecondRun_IsNoOp_NoDuplicates()
    {
        var profile = TenantSeedProfiles.BoNoiVu;

        await SeedGlobalCatalogAsync();
        await SeedTenantAsync(profile);
        var before = await CountAsync(profile);

        // Faithful second run through fresh contexts (mirrors a second startup).
        await SeedGlobalCatalogAsync();
        await SeedTenantAsync(profile);
        var after = await CountAsync(profile);

        before.Should().Be((1, 1, 1, 1));
        after.Should().Be((1, 1, 1, 1),
            "idempotency: a second run must not create duplicates of any seeded entity");
    }

    // ── T3: cross-tenant isolation — report def + widget share a name as distinct per-tenant rows ──

    [Fact]
    public async Task T3_SameNamedEntities_AcrossTenants_AreDistinctRows()
    {
        await SeedTenantAsync(TenantSeedProfiles.BoTaiChinh);
        await SeedTenantAsync(TenantSeedProfiles.BoNoiVu);

        await using var ctx = NewFilterFreeCtx();
        var btcSlug = TenantSeedProfiles.BoTaiChinh.TenantId.ToString();
        var bnvSlug = TenantSeedProfiles.BoNoiVu.TenantId.ToString();

        // Report definition: same machine Name, distinct tenant slug → two rows.
        var reports = await ctx.ReportDefinitions
            .IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Name == DemoReportingSeeder.DemoReportName
                        && (r.TenantId == btcSlug || r.TenantId == bnvSlug))
            .ToListAsync();
        reports.Should().HaveCount(2, "one report definition per tenant, scoped by the tenant slug");
        reports.Select(r => r.TenantId).Should().OnlyHaveUniqueItems("distinct slugs");

        // Widget: shared name (unique key is (Name, TenantId)) → two distinct rows.
        var widgets = await ctx.ReportWidgets
            .IgnoreQueryFilters().AsNoTracking()
            .Where(w => w.Name == DemoReportingSeeder.DemoWidgetName
                        && (w.TenantId == TenantSeedProfiles.BoTaiChinh.TenantId
                            || w.TenantId == TenantSeedProfiles.BoNoiVu.TenantId))
            .ToListAsync();
        widgets.Should().HaveCount(2, "one widget per tenant; a shared name is fine under (Name, TenantId)");
        widgets.Select(w => w.TenantId).Should().OnlyHaveUniqueItems("one per tenant");
        widgets.Select(w => w.Id).Should().OnlyHaveUniqueItems("distinct IDENTITY rows");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Drives the seeder's static per-tenant path through a fresh filter-free context (mirrors startup).</summary>
    private async Task SeedTenantAsync(TenantSeedProfile profile)
    {
        using var scope = _app.Services.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        await using var ctx = NewFilterFreeCtx();
        await DemoReportingSeeder.SeedTenantReportingAsync(
            ctx, profile, gen, NullLogger.Instance, CancellationToken.None);
    }

    /// <summary>Drives the global (tenant-less) catalog seed path.</summary>
    private async Task SeedGlobalCatalogAsync()
    {
        await using var ctx = NewFilterFreeCtx();
        await DemoReportingSeeder.SeedGlobalQueryCatalogAsync(ctx, NullLogger.Instance, CancellationToken.None);
    }

    private async Task<(int Reports, int Widgets, int Dashboards, int Catalog)> CountAsync(TenantSeedProfile profile)
    {
        await using var ctx = NewFilterFreeCtx();
        var slug = profile.TenantId.ToString();

        var reports = await ctx.ReportDefinitions
            .IgnoreQueryFilters().AsNoTracking()
            .CountAsync(r => r.TenantId == slug && r.Name == DemoReportingSeeder.DemoReportName);

        var widgets = await ctx.ReportWidgets
            .IgnoreQueryFilters().AsNoTracking()
            .CountAsync(w => w.TenantId == profile.TenantId && w.Name == DemoReportingSeeder.DemoWidgetName);

        var dashboards = await ctx.DashboardLayouts
            .IgnoreQueryFilters().AsNoTracking()
            .CountAsync(d => d.TenantId == profile.TenantId
                             && d.Name == $"Bảng điều khiển tổng quan (demo) — {profile.Name}");

        var catalog = await ctx.QueryCatalogEntries
            .IgnoreQueryFilters().AsNoTracking()
            .CountAsync(q => q.Code == DemoReportingSeeder.DemoQueryCatalogCode);

        return (reports, widgets, dashboards, catalog);
    }

    /// <summary>Filter-free ReportingDbContext (system seed context) — reads/writes span tenants like the seeder.</summary>
    private ReportingDbContext NewFilterFreeCtx() =>
        new(_app.Services.GetRequiredService<DbContextOptions<ReportingDbContext>>(),
            SystemSeederTenantContext.Instance);
}
