using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Seeding;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Organization;

/// <summary>
/// Real-SQL coverage for <see cref="TenantAssignmentSeeder"/> — the tenant-agnostic assignment +
/// position-history seeder (BTC + BNV).
///
/// Fixtures are realistic, not synthetic: real OrgUnits are inserted, then the proven
/// <see cref="TenantUserSeeder"/> populates users whose PrimaryOrgUnitId points at those units' PublicIds
/// — exactly the cross-schema shape the seeder resolves at runtime. The load-bearing facts: (1) every
/// seed user gets one assignment + one history (count + atomic-pair); (2) idempotent — a second pass
/// inserts nothing; (3) the orphan self-heal purges assignments whose UserId no longer exists (the
/// drift-reseed scenario where the user seeder recreates users with new GUIDs); (4) race-tolerant — a
/// tenant with no users yet is skipped, not thrown on; (5) the level-agnostic logic works on a shallow
/// BNV-shaped (2-level) tree; (6) two tenants stay isolated. Tests are non-vacuous — known counts on a
/// known tree, so a broken pass fails red rather than passing on an empty set.
///
/// All literals are tagged with the tenant id (shared tables, no per-test rollback); rows are cleaned up.
/// </summary>
[Collection("Integration")]
public class TenantAssignmentSeederTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private readonly string _connStr = db.SqlConnectionString;

    private static readonly TenantAssignmentSeederOptions AsgOpts = new()
    {
        Enabled = true,
        BatchSize = 500,
        OrphanPurgeCeiling = 25_000,
    };

    private static readonly TenantUserSeederOptions UserOpts = new()
    {
        Enabled = true,
        BatchSize = 500,
        RandomSeed = 4242,
        DevReserveCount = 10,
    };

    /// <summary>System tenant context — bypasses the global tenant filter so the seeder context can
    /// query/insert with an explicit tenant id (mirrors the internal SystemSeederTenantContext).</summary>
    private sealed class SystemSeederTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public bool IsSystemAdmin => true;
    }

    private OrgDbContext NewSeederContext(IServiceProvider sp) =>
        new(sp.GetRequiredService<DbContextOptions<OrgDbContext>>(), new SystemSeederTenantContext());

    [Fact]
    public async Task Seed_AssignsEverySeedUser_OneAssignmentAndHistoryEach()
    {
        var tenantId = Guid.NewGuid();
        var profile = MakeProfile(tenantId, userCount: 120);

        await using var scope = Factory.Services.CreateAsyncScope();
        try
        {
            var userTotal = await ArrangeOrgTreeAndUsersAsync(scope.ServiceProvider, tenantId, profile,
                (1, 1), (2, 6), (3, 12)); // 19 units, deep-ish tree
            userTotal.Should().BeGreaterThan(0, "fixture must seed users or the count assertions below are vacuous");

            await using var orgDb = NewSeederContext(scope.ServiceProvider);
            var (assignments, histories, usersPresent) = await TenantAssignmentSeeder.SeedAssignmentsForProfileAsync(
                _connStr, orgDb, profile, AsgOpts, NullLogger.Instance, default);

            usersPresent.Should().BeTrue("the tenant's users were seeded");
            assignments.Should().Be(userTotal, "one assignment per seed user (all have org + position)");
            histories.Should().Be(assignments, "assignment + history are inserted as an atomic pair");

            (await CountAssignmentsAsync(scope.ServiceProvider, tenantId)).Should().Be(userTotal);
            (await CountHistoriesAsync(scope.ServiceProvider, tenantId)).Should().Be(userTotal);
        }
        finally { await CleanupAsync(scope.ServiceProvider, tenantId); }
    }

    [Fact]
    public async Task Seed_RunTwice_SecondPassInsertsNothing()
    {
        var tenantId = Guid.NewGuid();
        var profile = MakeProfile(tenantId, userCount: 90);

        await using var scope = Factory.Services.CreateAsyncScope();
        try
        {
            var userTotal = await ArrangeOrgTreeAndUsersAsync(scope.ServiceProvider, tenantId, profile,
                (1, 1), (2, 5));
            userTotal.Should().BeGreaterThan(0, "fixture must seed users or the count assertions below are vacuous");

            await using (var orgDb = NewSeederContext(scope.ServiceProvider))
                await TenantAssignmentSeeder.SeedAssignmentsForProfileAsync(_connStr, orgDb, profile, AsgOpts, NullLogger.Instance, default);

            await using var orgDb2 = NewSeederContext(scope.ServiceProvider);
            var (assignments, histories, _) = await TenantAssignmentSeeder.SeedAssignmentsForProfileAsync(
                _connStr, orgDb2, profile, AsgOpts, NullLogger.Instance, default);

            assignments.Should().Be(0, "fast-path skip — every user already assigned");
            histories.Should().Be(0);
            (await CountAssignmentsAsync(scope.ServiceProvider, tenantId)).Should().Be(userTotal, "no duplicates after a second pass");
        }
        finally { await CleanupAsync(scope.ServiceProvider, tenantId); }
    }

    [Fact]
    public async Task Seed_PurgesOrphanAssignment_WhoseUserNoLongerExists()
    {
        var tenantId = Guid.NewGuid();
        var profile = MakeProfile(tenantId, userCount: 80);

        await using var scope = Factory.Services.CreateAsyncScope();
        try
        {
            var userTotal = await ArrangeOrgTreeAndUsersAsync(scope.ServiceProvider, tenantId, profile,
                (1, 1), (2, 4));
            userTotal.Should().BeGreaterThan(0, "fixture must seed users or the post-purge count assertion is vacuous");

            // Plant an orphan assignment + history for a user id that does NOT exist in AspNetUsers —
            // the drift-reseed scenario (user seeder recreated users with fresh GUIDs).
            var orphanUserId = Guid.NewGuid();
            await using (var orgDb = NewSeederContext(scope.ServiceProvider))
            {
                var anyOrgLongId = await orgDb.OrgUnits.IgnoreQueryFilters()
                    .Where(o => o.TenantId == tenantId).Select(o => o.Id).FirstAsync();
                orgDb.Assignments.Add(UserOrgUnitAssignment.Create(orphanUserId, anyOrgLongId, "Chuyên viên", tenantId));
                orgDb.PositionHistories.Add(StaffPositionHistory.Create(orphanUserId, anyOrgLongId, "Chuyên viên", tenantId, DateTimeOffset.UtcNow.AddYears(-1)));
                await orgDb.SaveChangesAsync();
            }

            (await CountAssignmentsAsync(scope.ServiceProvider, tenantId)).Should().Be(1, "the planted orphan exists pre-seed");

            await using (var orgDb = NewSeederContext(scope.ServiceProvider))
                await TenantAssignmentSeeder.SeedAssignmentsForProfileAsync(_connStr, orgDb, profile, AsgOpts, NullLogger.Instance, default);

            // The orphan is purged; only the real users remain assigned.
            (await CountAssignmentsAsync(scope.ServiceProvider, tenantId)).Should().Be(userTotal, "orphan purged, real users assigned");
            (await OrphanAssignmentExistsAsync(scope.ServiceProvider, tenantId, orphanUserId)).Should().BeFalse("the dangling-UserId assignment is gone");
        }
        finally { await CleanupAsync(scope.ServiceProvider, tenantId); }
    }

    [Fact]
    public async Task Seed_NoUsersForTenant_SkipsGracefully()
    {
        var tenantId = Guid.NewGuid();
        var profile = MakeProfile(tenantId, userCount: 50);
        // Deliberately seed NO users (and no org tree) for this tenant.

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var orgDb = NewSeederContext(scope.ServiceProvider);

        var act = async () => await TenantAssignmentSeeder.SeedAssignmentsForProfileAsync(
            _connStr, orgDb, profile, AsgOpts, NullLogger.Instance, default);

        var result = await act.Should().NotThrowAsync("a tenant with no users yet must converge over restart, not crash");
        result.Subject.Should().Be((0, 0, false), "no users present → signals retry, not completion");
    }

    [Fact]
    public async Task Seed_BnvShallowTree_AssignsUsers_LevelAgnostic()
    {
        // BNV post-merger: 1 root + 22 second-level units, no L3/L4 — the assignment logic reads only
        // PrimaryOrgUnitId + PositionName, so depth must not matter.
        var tenantId = Guid.NewGuid();
        var profile = MakeProfile(tenantId, userCount: 100);

        await using var scope = Factory.Services.CreateAsyncScope();
        try
        {
            var userTotal = await ArrangeOrgTreeAndUsersAsync(scope.ServiceProvider, tenantId, profile,
                (1, 1), (2, 22)); // 23 units, BNV shape
            userTotal.Should().BeGreaterThan(0, "fixture must seed users or the count assertion is vacuous");

            await using var orgDb = NewSeederContext(scope.ServiceProvider);
            var (assignments, _, _) = await TenantAssignmentSeeder.SeedAssignmentsForProfileAsync(
                _connStr, orgDb, profile, AsgOpts, NullLogger.Instance, default);

            assignments.Should().Be(userTotal, "a 2-level tree assigns identically to a deep one");
            (await CountAssignmentsAsync(scope.ServiceProvider, tenantId)).Should().Be(userTotal);
        }
        finally { await CleanupAsync(scope.ServiceProvider, tenantId); }
    }

    [Fact]
    public async Task Seed_TwoTenants_AssignmentsStayTenantScoped()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var profileA = MakeProfile(tenantA, userCount: 70);
        var profileB = MakeProfile(tenantB, userCount: 70);

        await using var scope = Factory.Services.CreateAsyncScope();
        try
        {
            var totalA = await ArrangeOrgTreeAndUsersAsync(scope.ServiceProvider, tenantA, profileA, (1, 1), (2, 4));
            var totalB = await ArrangeOrgTreeAndUsersAsync(scope.ServiceProvider, tenantB, profileB, (1, 1), (2, 4));
            totalA.Should().BeGreaterThan(0, "fixture must seed users or the per-tenant count assertions are vacuous");
            totalB.Should().BeGreaterThan(0);

            await using (var orgDb = NewSeederContext(scope.ServiceProvider))
                await TenantAssignmentSeeder.SeedAssignmentsForProfileAsync(_connStr, orgDb, profileA, AsgOpts, NullLogger.Instance, default);
            await using (var orgDb = NewSeederContext(scope.ServiceProvider))
                await TenantAssignmentSeeder.SeedAssignmentsForProfileAsync(_connStr, orgDb, profileB, AsgOpts, NullLogger.Instance, default);

            (await CountAssignmentsAsync(scope.ServiceProvider, tenantA)).Should().Be(totalA, "tenant A assignments unaffected by B");
            (await CountAssignmentsAsync(scope.ServiceProvider, tenantB)).Should().Be(totalB, "tenant B assignments unaffected by A");
        }
        finally
        {
            await CleanupAsync(scope.ServiceProvider, tenantA);
            await CleanupAsync(scope.ServiceProvider, tenantB);
        }
    }

    // ── Arrange / helpers ──────────────────────────────────────────────────────

    /// <summary>Inserts a real org tree + seeds users (via the proven user seeder) whose PrimaryOrgUnitId
    /// points at the tree's PublicIds — the exact cross-schema shape the assignment seeder resolves.
    /// Returns the seeded user count.</summary>
    private async Task<int> ArrangeOrgTreeAndUsersAsync(
        IServiceProvider sp, Guid tenantId, TenantSeedProfile profile, params (int Level, int Count)[] tiers)
    {
        var gen = sp.GetRequiredService<IGuidGenerator>();
        var tag = tenantId.ToString("N")[..8];

        var orgRows = new List<TenantUserSeeder.OrgUnitRow>();
        await using (var orgDb = NewSeederContext(sp))
        {
            foreach (var (level, count) in tiers)
                for (int i = 0; i < count; i++)
                {
                    var code = $"{tag}.L{level}.{i:D3}";
                    var unit = OrgUnit.Create(gen, $"Unit {code}", $"Unit {code}", code, tenantId, parentId: null, level: level);
                    orgDb.OrgUnits.Add(unit);
                }
            await orgDb.SaveChangesAsync();
        }

        // Re-read to capture the persisted PublicId + Level (PublicId is the user's PrimaryOrgUnitId).
        await using (var orgDb = NewSeederContext(sp))
        {
            orgRows = await orgDb.OrgUnits.IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId)
                .Select(o => new TenantUserSeeder.OrgUnitRow { Id = o.PublicId, Code = o.Code, Level = o.Level })
                .ToListAsync();
        }

        var ctx = sp.GetRequiredService<IdentityDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        return await TenantUserSeeder.SeedUsersForProfileAsync(
            ctx, hasher, profile, UserOpts, orgRows, "DevAdmin@12345", NullLogger.Instance, default);
    }

    private static TenantSeedProfile MakeProfile(Guid tenantId, int userCount)
    {
        var tag = tenantId.ToString("N")[..8];
        return new TenantSeedProfile
        {
            TenantId = tenantId,
            Code = $"ZZ-{tag}",
            Name = $"Synthetic {tag}",
            RolePrefix = $"ZZ{tag}_",
            UserCodePrefix = $"ZZ-{tag}-",
            LeadEmailPrefix = $"zz.{tag}.lead.",
            DevEmailPrefix = $"zz.{tag}.dev.",
            FillEmailPrefix = $"zz.{tag}.",
            EmailDomain = $"@zz-{tag}.test",
            UserCount = userCount,
            MaxLevel = 4,
            SeedVersion = "test",
        };
    }

    private async Task<int> CountAssignmentsAsync(IServiceProvider sp, Guid tenantId)
    {
        await using var orgDb = NewSeederContext(sp);
        return await orgDb.Assignments.IgnoreQueryFilters().CountAsync(a => a.TenantId == tenantId);
    }

    private async Task<int> CountHistoriesAsync(IServiceProvider sp, Guid tenantId)
    {
        await using var orgDb = NewSeederContext(sp);
        return await orgDb.PositionHistories.IgnoreQueryFilters().CountAsync(h => h.TenantId == tenantId);
    }

    private async Task<bool> OrphanAssignmentExistsAsync(IServiceProvider sp, Guid tenantId, Guid userId)
    {
        await using var orgDb = NewSeederContext(sp);
        return await orgDb.Assignments.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenantId && a.UserId == userId);
    }

    private async Task CleanupAsync(IServiceProvider sp, Guid tenantId)
    {
        await using (var orgDb = NewSeederContext(sp))
        {
            await orgDb.Assignments.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).ExecuteDeleteAsync();
            await orgDb.PositionHistories.IgnoreQueryFilters().Where(h => h.TenantId == tenantId).ExecuteDeleteAsync();
            await orgDb.OrgUnits.IgnoreQueryFilters().Where(o => o.TenantId == tenantId).ExecuteDeleteAsync();
        }

        var ctx = sp.GetRequiredService<IdentityDbContext>();
        var userIds = ctx.Users.Where(u => u.TenantId == tenantId).Select(u => u.Id);
        await ctx.UserRoles.Where(ur => userIds.Contains(ur.UserId)).ExecuteDeleteAsync();
        await ctx.Users.Where(u => u.TenantId == tenantId).ExecuteDeleteAsync();
    }
}
