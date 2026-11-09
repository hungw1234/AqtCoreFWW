using System.Globalization;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.Reporting.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Reporting.Infrastructure.Seeding;

/// <summary>
/// Tenant-agnostic demo reporting seeder. Per <see cref="TenantSeedProfiles"/> entry (BTC + BNV) it
/// stages one <see cref="ReportDefinition"/> + one <see cref="ReportWidget"/> + one
/// <see cref="DashboardLayout"/> whose grid references the seeded widget; plus a single GLOBAL
/// <see cref="QueryCatalogEntry"/> (the NLQ catalog is system-wide, not tenant-scoped). Definitions
/// only — no <c>ReportExecution</c> runtime rows.
///
/// <para>The module has three different tenant-scope shapes and the seeder honours each:
/// <see cref="ReportDefinition.TenantId"/> is a string slug (the Guid as text — that is what the
/// query handler compares against, so a tenant user actually sees the row); <see cref="ReportWidget"/>
/// and <see cref="DashboardLayout"/> are Guid <c>ITenantScoped</c> (EF global filter applies);
/// <see cref="QueryCatalogEntry"/> has no tenant key at all and is seeded once.</para>
///
/// <para>Widget vs dashboard naming is asymmetric on purpose: the widget unique key is
/// <c>(Name, TenantId)</c> so a shared name across tenants is fine and keeps the cross-tenant rows
/// distinct by TenantId; the dashboard unique key is <c>(Name, OwnerId)</c> and the seeder owns every
/// demo dashboard as the system actor, so the dashboard name must be tenant-distinct or two tenants
/// would collide on the same <c>(Name, Guid.Empty)</c>.</para>
///
/// <para>All seeded SQL (report template, widget query, catalog query) is a self-contained, fully
/// parameterized literal that carries <c>@TenantId</c> and touches no table — the widget and catalog
/// paths run these strings through Dapper without SQL validation, so the demo deliberately avoids any
/// schema dependency and stays valid regardless of which executor runs it.</para>
///
/// <para>Config-gated (<c>Reporting:DemoReportingSeed</c>, default OFF) and idempotent. Self-contained:
/// no cross-module FK, so this is module-resident (mirrors the Workflow / Forms / Rules / Integration
/// demo seeders). Cross-tenant reads/writes from a single startup scope rely on the dev/seed connection
/// being db_owner, which bypasses any RLS predicate (the same mechanism the sibling seeders use);
/// <see cref="SystemSeederTenantContext"/> leaves SESSION_CONTEXT unset and reports IsSystemAdmin so the
/// EF global filter is bypassed too. None of the four <c>Create</c> factories raise a domain event, so
/// no outbox row escapes.</para>
/// </summary>
public sealed class DemoReportingSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<DemoReportingSeederOptions> options,
    IGuidGenerator gen,
    ILogger<DemoReportingSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly DemoReportingSeederOptions _opts = options.Value;

    /// <summary>Stable report machine name (the idempotency key together with the tenant slug).</summary>
    public const string DemoReportName = "DemoCaseSummaryReport";

    /// <summary>Stable widget name — shared across tenants (unique key is (Name, TenantId)).</summary>
    public const string DemoWidgetName = "Tổng số hồ sơ (demo)";

    /// <summary>Stable catalog code — globally unique (the catalog has no tenant key).</summary>
    public const string DemoQueryCatalogCode = "DEMO_CASE_SUMMARY";

    // Seeder-authored rows are attributed to the system actor (all-zero Guid, mirrors the sibling seeders).
    private static readonly Guid SystemActorId = Guid.Empty;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opts.Enabled)
        {
            logger.LogDebug("[DemoReportingSeeder] Disabled — skipping reporting demo seed");
            return;
        }

        // Global, tenant-less catalog entry first (seeded once regardless of tenant count).
        using (var globalScope = scopeFactory.CreateScope())
        {
            await using var globalCtx = NewSeedContext(globalScope);
            await SeedGlobalQueryCatalogAsync(globalCtx, logger, ct);
        }

        foreach (var profile in TenantSeedProfiles.All)
        {
            using var scope = scopeFactory.CreateScope();
            await using var ctx = NewSeedContext(scope);
            await SeedTenantReportingAsync(ctx, profile, gen, logger, ct);
        }

        logger.LogInformation("[DemoReportingSeeder] All tenants complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Builds a <see cref="ReportingDbContext"/> on the system seed context so the tenant global filter is
    /// bypassed — the idempotency probes + inserts span both tenants from one startup scope.
    /// </summary>
    private static ReportingDbContext NewSeedContext(IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<DbContextOptions<ReportingDbContext>>(),
            SystemSeederTenantContext.Instance);

    /// <summary>
    /// Seeds the demo reporting set for one tenant. Idempotent: the anchor probe is the
    /// <see cref="ReportDefinition"/> keyed by (Name, tenant slug) — because the per-tenant block is one
    /// transaction, the report definition's presence means the whole block already ran. Two-phase save:
    /// (1) the report definition + the widget in one SaveChanges (independent inserts); (2) the dashboard,
    /// whose grid JSON must reference the now-assigned widget.Id (the widgets are linked by id-in-JSON, not
    /// an FK navigation, so the id is only known after phase 1). Both phases are wrapped in one transaction
    /// so a phase-2 fault cannot leave a committed report whose anchor probe would skip the tenant forever
    /// on a plain restart. Static + ctx-injected so tests can drive it directly.
    /// </summary>
    public static async Task SeedTenantReportingAsync(
        ReportingDbContext ctx,
        TenantSeedProfile profile,
        IGuidGenerator gen,
        ILogger logger,
        CancellationToken ct)
    {
        // ReportDefinition.TenantId is the tenant slug = the Guid as text (ReportsController passes
        // tenantContext.TenantId?.ToString() to the query handler), so seed exactly that to be visible.
        var tenantSlug = profile.TenantId.ToString();

        // IgnoreQueryFilters drops the soft-delete filter too, so a soft-deleted demo report is treated as
        // present and not resurrected (intended). ReportDefinition is not ITenantScoped, so the slug match
        // is a plain column compare — backed by the DB unique (Name, TenantId) WHERE TenantId IS NOT NULL.
        var exists = await ctx.ReportDefinitions
            .IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == tenantSlug && r.Name == DemoReportName, ct);
        if (exists)
        {
            logger.LogInformation(
                "[DemoReportingSeeder] {Code}: demo reporting set already present — skipping", profile.Code);
            return;
        }

        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        // ── Phase 1: report definition (tenant slug) + KPI widget (Guid tenant) — independent inserts ──
        var reportDefinition = ReportDefinition.Create(
            gen,
            name: DemoReportName,
            nameVi: "Báo cáo tổng hợp hồ sơ (demo)",
            description: $"Báo cáo tổng hợp demo cho {profile.Name}.",
            // Self-contained parameterized template — carries @TenantId, touches no table.
            sqlTemplate: "SELECT CAST(0 AS bigint) AS TongSoHoSo WHERE @TenantId IS NOT NULL;",
            parametersSchema: "[]",
            format: OutputFormat.Both,
            tenantId: tenantSlug);

        var widget = ReportWidget.Create(
            name: DemoWidgetName,
            type: WidgetType.KPI,
            dataQuery: "SELECT CAST(0 AS bigint) AS [Value] WHERE @TenantId IS NOT NULL;",
            tenantId: profile.TenantId,
            createdBy: SystemActorId,
            config: """{"format":"number","label":"Tổng số hồ sơ"}""",
            refreshSeconds: 300);

        await ctx.ReportDefinitions.AddAsync(reportDefinition, ct);
        await ctx.ReportWidgets.AddAsync(widget, ct);
        await ctx.SaveChangesAsync(ct); // widget.Id assigned by IDENTITY — needed for the grid JSON below

        // ── Phase 2: dashboard whose grid references the assigned widget.Id (id-in-JSON, not an FK) ─────
        var dashboard = DashboardLayout.Create(
            // Tenant-distinct name — the unique key is (Name, OwnerId) and every demo dashboard shares
            // OwnerId = Guid.Empty, so the name must differ per tenant.
            name: $"Bảng điều khiển tổng quan (demo) — {profile.Name}",
            ownerId: SystemActorId,
            tenantId: profile.TenantId,
            isDefault: false);

        dashboard.UpdateLayout(BuildWidgetGridJson(widget.Id));

        await ctx.DashboardLayouts.AddAsync(dashboard, ct);
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "[DemoReportingSeeder] {Code}: seeded 1 report definition + 1 widget + 1 dashboard", profile.Code);
    }

    /// <summary>
    /// Seeds the single global NLQ catalog entry. <see cref="QueryCatalogEntry"/> has no tenant key, so it
    /// is seeded once (outside the per-tenant loop) and is idempotent on its globally-unique Code. A single
    /// insert needs no transaction.
    /// </summary>
    public static async Task SeedGlobalQueryCatalogAsync(
        ReportingDbContext ctx,
        ILogger logger,
        CancellationToken ct)
    {
        var exists = await ctx.QueryCatalogEntries
            .IgnoreQueryFilters()
            .AnyAsync(q => q.Code == DemoQueryCatalogCode, ct);
        if (exists)
        {
            logger.LogInformation("[DemoReportingSeeder] global query catalog entry already present — skipping");
            return;
        }

        var entry = QueryCatalogEntry.Create(
            code: DemoQueryCatalogCode,
            description: "Demo case-summary count query.",
            descriptionVi: "Truy vấn đếm tổng hợp hồ sơ (demo).",
            // Self-contained parameterized query — carries @TenantId, touches no table.
            dapperSql: "SELECT CAST(0 AS bigint) AS TongSoHoSo WHERE @TenantId IS NOT NULL;",
            requiredParams: "[]",
            optionalParams: "[]",
            requiredRole: null);

        await ctx.QueryCatalogEntries.AddAsync(entry, ct);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("[DemoReportingSeeder] seeded global query catalog entry {Code}", DemoQueryCatalogCode);
    }

    /// <summary>
    /// Builds the dashboard grid JSON for a single seeded widget. widgetId is the ReportWidget.long Id as
    /// text — that is what GetWidgetData resolves the JSON reference against (the widget has no PublicId).
    /// </summary>
    private static string BuildWidgetGridJson(long widgetId) =>
        string.Format(
            CultureInfo.InvariantCulture,
            """[{{"widgetId":"{0}","x":0,"y":0,"w":6,"h":4}}]""",
            widgetId);
}
