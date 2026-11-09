using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Integration.Infrastructure.Seeding;

/// <summary>
/// Tenant-agnostic demo integration seeder: seeds one Active <see cref="Partner"/> plus one Draft
/// <see cref="Contract"/> per <see cref="TenantSeedProfiles"/> entry (BTC + BNV). Definitions only —
/// no <c>MessageLog</c> (a runtime audit-log entity written by message handlers, never seeded).
///
/// <para>Config-gated (<c>Integration:DemoIntegrationSeed</c>, default OFF) and idempotent per
/// (TenantId, Code) — the unique (TenantId, Code) index (filtered on IsDeleted = 0) makes the
/// existence probe load-bearing. Self-contained: Partner/Contract carry no cross-module FK, so unlike
/// the BusinessProcess seeder this needs no cross-module dependency resolution and no host residency.
/// There is no tenant-existence guard — a Partner's TenantId is an unenforced Guid stamp (no FK), so a
/// tenant with no row yields a harmless never-surfaced partner rather than an orphaned-FK row.</para>
///
/// <para>Cross-tenant reads/writes from a single startup scope rely on the dev/seed connection being
/// db_owner (the RLS predicate bypasses for db_owner; <see cref="SystemSeederTenantContext"/> leaves
/// SESSION_CONTEXT unset and reports IsSystemAdmin so the EF global filter is bypassed too). Mirrors
/// the Workflow / Forms demo seeders.</para>
///
/// <para>Note: <see cref="Partner.Create"/> raises an internal-only PartnerCreatedEvent (no external
/// outbox escape — it is not an IExternalDomainEvent). No handler is registered for it today, so the
/// seed dispatch is a no-op; a maintainer who later adds a handler should be aware it would then fire
/// on every dev seed.</para>
/// </summary>
public sealed class DemoIntegrationSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<DemoIntegrationSeederOptions> options,
    IGuidGenerator gen,
    ILogger<DemoIntegrationSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly DemoIntegrationSeederOptions _opts = options.Value;

    /// <summary>Stable partner code (shared across tenants; uniqueness is per TenantId).</summary>
    public const string DemoPartnerCode = "demo-partner";

    // Seeder-authored rows are attributed to the system actor (all-zero Guid, mirrors E2E seeder).
    private static readonly Guid SystemActorId = Guid.Empty;

    // Fixed effective date so re-seeds (and the demo) are deterministic — never DateTime.UtcNow.
    private static readonly DateTime DemoEffectiveDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opts.Enabled)
        {
            logger.LogDebug("[DemoIntegrationSeeder] Disabled — skipping integration demo seed");
            return;
        }

        foreach (var profile in TenantSeedProfiles.All)
        {
            using var scope = scopeFactory.CreateScope();
            var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<IntegrationDbContext>>();

            // Construct with the system seed context so the tenant global filter is bypassed — the
            // idempotency probe + inserts span both tenants from one startup scope (mirror the other
            // demo seeders).
            await using var ctx = new IntegrationDbContext(dbOptions, SystemSeederTenantContext.Instance);

            await SeedTenantIntegrationAsync(ctx, profile, gen, logger, ct);
        }

        logger.LogInformation("[DemoIntegrationSeeder] All tenants complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Seeds the demo partner + contract for one tenant. Idempotent per (TenantId, Code): a second run
    /// is a no-op. Two-phase save (EF IDENTITY-before-children): (1) the Partner root is inserted so its
    /// Id is assigned, then (2) the Contract — keyed by the now-assigned partner.Id (Contract has no
    /// collection navigation on Partner, so the FK cannot be EF-fixed up; it is a genuine two-phase).
    /// Both phases are wrapped in one transaction so a phase-2 fault cannot leave a committed Partner
    /// with no Contract: the idempotency probe keys on the Partner, so a half-wired survivor would be
    /// skipped (never repaired) on a plain restart. Static + ctx-injected so tests can drive it directly.
    /// </summary>
    public static async Task SeedTenantIntegrationAsync(
        IntegrationDbContext ctx,
        TenantSeedProfile profile,
        IGuidGenerator gen,
        ILogger logger,
        CancellationToken ct)
    {
        // IgnoreQueryFilters drops both the tenant filter (so one scope spans tenants) and the
        // soft-delete filter, so the probe also sees tombstoned rows: a soft-deleted demo partner is
        // treated as present and not resurrected on the next run — the intended demo behaviour.
        var exists = await ctx.Partners
            .IgnoreQueryFilters()
            .AnyAsync(p => p.TenantId == profile.TenantId && p.Code == DemoPartnerCode, ct);
        if (exists)
        {
            logger.LogInformation(
                "[DemoIntegrationSeeder] {Code}: demo partner already present — skipping", profile.Code);
            return;
        }

        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        // ── Phase 1: Partner root (Active) ──────────────────────────────────────────────────────────
        var partner = Partner.Create(
            gen,
            tenantId: profile.TenantId,
            name: $"Đối tác tích hợp (demo) — {profile.Name}",
            code: DemoPartnerCode,
            createdBy: SystemActorId,
            contactEmail: "tichhop.demo@partner.test",
            contactPhone: "+84-000-000-000",
            endpoint: "https://partner.demo.local/api",
            authScheme: "ApiKey");

        await ctx.Partners.AddAsync(partner, ct);
        await ctx.SaveChangesAsync(ct); // partner.Id assigned by IDENTITY

        // ── Phase 2: Contract (Draft) keyed by the assigned partner.Id ──────────────────────────────
        var contract = Contract.Create(
            gen,
            tenantId: profile.TenantId,
            partnerId: partner.Id,
            title: "Hợp đồng trao đổi dữ liệu (demo)",
            description: $"Hợp đồng demo trao đổi dữ liệu cho {profile.Name}.",
            effectiveDate: DemoEffectiveDate,
            expiryDate: null,
            dataScopeJson: """{"scopes":["read:masterdata"]}""",
            createdBy: SystemActorId);

        await ctx.Contracts.AddAsync(contract, ct);
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "[DemoIntegrationSeeder] {Code}: seeded demo partner '{Partner}' (Active) + 1 contract (Draft)",
            profile.Code, DemoPartnerCode);
    }
}
