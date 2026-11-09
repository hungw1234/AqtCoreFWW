using AqtCoreFW.Api;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Smoke test for the menu seeder orphan-purge step [MTS-3, 2026-05-12].
///
/// <see cref="E2ETestDataSeeder"/>'s per-row idempotency only ADDS missing menu rows; structure
/// entries removed from <c>BuildMenuList()</c> previously lingered in the DB and surfaced as
/// FE-sidebar 404s (the "admin-overview" leaf, bug #14). <c>PurgeOrphanMenusAsync</c> now deletes
/// any seeder-owned row (PublicId in the <c>60000000-0000-0000-0000-*</c> family, system-scoped
/// <c>TenantId == null</c>) that is no longer desired — children before parents (self-ref FK is
/// <c>Restrict</c>), <c>MenuRolePermissions</c> cascading.
///
/// This test boots the full Program host (first seeder pass → DB migrated + canonical structure),
/// injects three orphan rows + one admin-created menu, re-invokes the seeder, then asserts:
///   • orphan parent + child rows gone (delete ordering works);
///   • the historical "admin-overview" PublicId row gone;
///   • the admin-created (non-family-prefix) menu survives;
///   • the orphan child's permission row cascaded away;
///   • a canonical menu ("dashboard") is untouched.
///
/// Tag: [Trait("Category","Smoke")] — excluded from fast-feedback runs, mandatory in CI finalize.
/// </summary>
[Collection(SmokeTestCollection.CollectionName)]
public sealed class MenuSeederOrphanPurgeSmokeTests : IAsyncLifetime
{
    // 60000000-* family → eligible for purge. Last byte 0xFE / 0xFF: not used by BuildMenuList.
    private static readonly Guid OrphanParentId      = Guid.Parse("60000000-0000-0000-0000-0000000000fe");
    private static readonly Guid OrphanChildId       = Guid.Parse("60000000-0000-0000-0000-0000000000ff");
    // The real removed leaf (= the un-seeded MenuAdminOverviewId constant): must be auto-purged.
    private static readonly Guid HistoricalOrphanId  = Guid.Parse("60000000-0000-0000-0000-000000000030");
    // Outside the seeded family (mimics CreateMenuCommandHandler → Guid v7): must survive.
    private static readonly Guid AdminCreatedMenuId  = Guid.Parse("7f000000-0000-0000-0000-000000000001");

    private static readonly Guid[] TestRowPublicIds =
        [OrphanChildId, HistoricalOrphanId, AdminCreatedMenuId, OrphanParentId]; // child-before-parent for cleanup

    private readonly SqlServerFixture _sqlServer;
    private SeederSmokeFixture? _fixture;

    public MenuSeederOrphanPurgeSmokeTests(SqlServerFixture sqlServer) => _sqlServer = sqlServer;

    public async Task InitializeAsync()
    {
        _fixture = new SeederSmokeFixture(_sqlServer);
        await ((IAsyncLifetime)_fixture).InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
            await ((IAsyncLifetime)_fixture).DisposeAsync();
    }

    [Fact, Trait("Category", "Smoke")]
    public async Task SeedMenus_PurgesOrphansInSeededFamily_KeepsAdminCreatedMenus()
    {
        // Boot host → first seeder pass migrates the DB and seeds the canonical menu structure.
        using (var _ = _fixture!.CreateClient()) { }

        await RemoveTestRowsAsync();        // clean leftovers from a prior run (shared DB)
        await InsertOrphanFixtureRowsAsync();

        // Re-invoke the seeder (idempotent) — this triggers PurgeOrphanMenusAsync.
        var seeder = _fixture.Services.GetServices<IHostedService>().OfType<E2ETestDataSeeder>().Single();
        await seeder.StartAsync(CancellationToken.None);

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            Assert.Null(await db.AppMenus.AsNoTracking().FirstOrDefaultAsync(m => m.PublicId == OrphanParentId));
            Assert.Null(await db.AppMenus.AsNoTracking().FirstOrDefaultAsync(m => m.PublicId == OrphanChildId));
            Assert.Null(await db.AppMenus.AsNoTracking().FirstOrDefaultAsync(m => m.PublicId == HistoricalOrphanId));

            var admin = await db.AppMenus.AsNoTracking().FirstOrDefaultAsync(m => m.PublicId == AdminCreatedMenuId);
            Assert.NotNull(admin);

            Assert.False(await db.Set<MenuRolePermission>().AsNoTracking()
                .AnyAsync(rp => rp.PermissionCode == "TEST.ORPHAN.VIEW"));

            Assert.NotNull(await db.AppMenus.AsNoTracking().FirstOrDefaultAsync(m => m.Code == "dashboard"));
        }

        await RemoveTestRowsAsync(); // tidy the shared DB (admin-created row is never auto-purged)
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task InsertOrphanFixtureRowsAsync()
    {
        using var scope = _fixture!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        db.AppMenus.Add(Menu(OrphanParentId, "test-orphan-parent", "Orphan Parent", parentId: null, route: null, sort: 990));
        await db.SaveChangesAsync(); // parent first — child FK targets parent.PublicId

        db.AppMenus.Add(Menu(OrphanChildId, "test-orphan-child", "Orphan Child", parentId: OrphanParentId, route: "/test/orphan", sort: 991));
        db.AppMenus.Add(Menu(HistoricalOrphanId, "admin-overview", "Tong quan admin", parentId: null, route: "/admin", sort: 992));
        db.AppMenus.Add(Menu(AdminCreatedMenuId, "test-admin-created", "Admin Created", parentId: null, route: "/admin/custom", sort: 993));
        await db.SaveChangesAsync();

        var childLongId = await db.AppMenus.Where(m => m.PublicId == OrphanChildId).Select(m => m.Id).SingleAsync();
        db.Set<MenuRolePermission>().Add(new MenuRolePermission { MenuId = childLongId, PermissionCode = "TEST.ORPHAN.VIEW" });
        await db.SaveChangesAsync();
    }

    private async Task RemoveTestRowsAsync()
    {
        using var scope = _fixture!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // ExecuteDeleteAsync bypasses SoftDeleteInterceptor — mirrors PurgeOrphanMenusAsync's fix
        // (MTS-3-FU3). RemoveRange + SaveChanges here would soft-delete the AppMenu, leaving its
        // MenuRolePermissions orphaned and the AdminCreatedMenuId row accumulating in the shared
        // SqlServerFixture DB on every run. IgnoreQueryFilters so we also tidy rows already
        // soft-deleted by older runs. One-by-one in TestRowPublicIds order (child-before-parent)
        // satisfies the AppMenu.ParentId self-ref FK (Restrict).
        foreach (var publicId in TestRowPublicIds)
        {
            await db.AppMenus.IgnoreQueryFilters()
                .Where(m => m.PublicId == publicId)
                .ExecuteDeleteAsync();
        }
    }

    private static AppMenu Menu(Guid publicId, string code, string title, Guid? parentId, string? route, int sort)
        => AppMenu.Create(new PinnedGen(publicId), code, title, parentId,
            description: null, icon: null, route: route, sortOrder: sort, tenantId: null, actorId: Guid.Empty);

    private sealed class PinnedGen(Guid g) : IGuidGenerator
    {
        public Guid NewIdMonotonic() => g;
        public Guid NewIdRandom() => g;
    }
}
