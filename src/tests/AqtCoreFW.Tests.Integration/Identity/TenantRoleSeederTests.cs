using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Seeding;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Real-SQL coverage for <see cref="TenantRoleSeeder"/> — the tenant-agnostic business-role seeder.
///
/// The load-bearing fact is multi-tenancy: two tenants seed the SAME 17 role display names (differing
/// only by code prefix) and BOTH must succeed. That only works because the AspNetRoles NormalizedName
/// uniqueness is scoped to (TenantId, NormalizedName), not global (the ScopeRoleNameToTenant migration).
/// The seeder inserts directly via db.Roles.Add + SaveChangesAsync (not RoleManager), so under the
/// legacy global RoleNameIndex the second tenant's SaveChanges would throw a DbUpdateException on the
/// NormalizedName unique-violation — the test would fail red, not silently pass; hence non-vacuous.
///
/// Synthetic tenant GUIDs + "ZZ"-prefixed codes keep the shared test DB free of collisions with real
/// seeded roles; the roles are cleaned up afterwards.
/// </summary>
[Collection("Integration")]
public class TenantRoleSeederTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task SeedRoles_TwoTenantsShareRoleNames_BothSucceed_AndIdempotent()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var normalizer = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>();

        try
        {
            // ── Two tenants, identical role names, distinct prefixes ───────────────
            var createdA = await TenantRoleSeeder.SeedRolesAsync(
                db, normalizer, tenantA, "ZZA_", NullLogger.Instance, CancellationToken.None);
            var createdB = await TenantRoleSeeder.SeedRolesAsync(
                db, normalizer, tenantB, "ZZB_", NullLogger.Instance, CancellationToken.None);

            createdA.Should().Be(TenantRoleSeeder.RoleCount);
            createdB.Should().Be(TenantRoleSeeder.RoleCount,
                "both tenants must seed the same role names — uniqueness is per (TenantId, NormalizedName), not global");

            // ── Shape: 17 prefixed business roles per tenant ───────────────────────
            var rolesA = await db.Roles.Where(r => r.TenantId == tenantA).ToListAsync();
            rolesA.Should().HaveCount(TenantRoleSeeder.RoleCount);
            rolesA.Should().OnlyContain(r => r.Code.StartsWith("ZZA_") && r.RoleType == RoleType.Business && r.IsActive);
            rolesA.Should().Contain(r => r.Code == "ZZA_MINISTER" && r.Name == "Bộ trưởng",
                "Vietnamese role names round-trip through the nvarchar column");

            // ── The exact collision the migration fixes: same display name, two tenants ──
            var ministerAcrossTenants = await db.Roles
                .CountAsync(r => r.Name == "Bộ trưởng" && (r.TenantId == tenantA || r.TenantId == tenantB));
            ministerAcrossTenants.Should().Be(2, "the same role name coexists under two distinct tenants");

            // ── Idempotency: a second pass for tenant A inserts nothing ────────────
            var createdAgain = await TenantRoleSeeder.SeedRolesAsync(
                db, normalizer, tenantA, "ZZA_", NullLogger.Instance, CancellationToken.None);
            createdAgain.Should().Be(0, "re-running must skip existing roles (matched by Code + TenantId)");
            (await db.Roles.CountAsync(r => r.TenantId == tenantA))
                .Should().Be(TenantRoleSeeder.RoleCount);
        }
        finally
        {
            // AspNetRoles is shared (no per-test rollback) — remove the synthetic-tenant roles.
            await db.Roles.Where(r => r.TenantId == tenantA || r.TenantId == tenantB)
                .ExecuteDeleteAsync();
        }
    }
}
