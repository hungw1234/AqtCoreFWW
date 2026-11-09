using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Real-SQL coverage for <see cref="TenantUserSeeder"/> — the tenant-agnostic 3-pass user + user-role
/// seeder. The seeder's static core is driven directly with a SYNTHETIC org tree (no Organization
/// schema dependency, since ApplicationUser.PrimaryOrgUnitId carries no cross-schema FK).
///
/// The load-bearing facts: (1) 3-pass invariants hold across DIFFERENT tree shapes — a 4-level
/// BTC-like tree and a 2-level/22-unit BNV-like tree (the shape the algorithm was never originally
/// exercised on); (2) the deterministic lead/dev emails downstream depends on are preserved; (3) two
/// tenants coexist without email collision; (4) per-tenant faker isolation keeps each tenant's output
/// reproducible regardless of run order. Tests are non-vacuous: a synthetic tree with KNOWN counts so
/// a broken pass fails red, not passes on an empty set.
///
/// All identity literals are tagged with the synthetic tenant id so AspNetUsers / AspNetRoles (shared,
/// no per-test rollback) never collide across tests or with real seeded data; rows are cleaned up after.
/// </summary>
[Collection("Integration")]
public class TenantUserSeederTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private static readonly TenantUserSeederOptions Opts = new()
    {
        Enabled = true,
        BatchSize = 500,
        RandomSeed = 12345,
        DevReserveCount = 20,
    };

    [Fact]
    public async Task Seed_FourLevelTree_HoldsThreePassInvariants_AndDeterministicEmails()
    {
        var tenantId = Guid.NewGuid();
        var profile = MakeProfile(tenantId, userCount: 300);
        var tree = BuildTree(tenantId, (1, 1), (2, 5), (3, 20), (4, 40)); // 66 units

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();

        try
        {
            var total = await TenantUserSeeder.SeedUsersForProfileAsync(
                ctx, hasher, profile, Opts, tree, "DevAdmin@12345", NullLogger.Instance, default);

            // Pass 1 — exactly one lead per unit.
            var leadCount = await ctx.Users.CountAsync(u =>
                u.TenantId == tenantId && u.UserName!.StartsWith(profile.LeadEmailPrefix));
            leadCount.Should().Be(tree.Count, "Pass 1 seeds exactly one lead per org unit");

            // Pass 3 — exactly DevReserveCount dev users (an L2 tier exists to pin them to).
            var devCount = await ctx.Users.CountAsync(u =>
                u.TenantId == tenantId && u.UserName!.StartsWith(profile.DevEmailPrefix));
            devCount.Should().Be(Opts.DevReserveCount, "Pass 3 pins the dev reserve to random L2 units");

            // The remainder top-up lands the tenant exactly on target, so a restart hits the "at target" gate.
            total.Should().Be(profile.UserCount);
            (await ctx.Users.CountAsync(u => u.TenantId == tenantId)).Should().Be(profile.UserCount);

            // Deterministic lead email for the single L1 root — the contract downstream relies on.
            var rootCode = tree.Single(o => o.Level == 1).Code;
            var rootLeadEmail = $"{profile.LeadEmailPrefix}{rootCode.ToLowerInvariant().Replace('.', '-')}{profile.EmailDomain}";
            (await ctx.Users.AnyAsync(u => u.UserName == rootLeadEmail))
                .Should().BeTrue("the L1 lead email is deterministic from the org code slug");
        }
        finally { await CleanupAsync(ctx, tenantId); }
    }

    [Fact]
    public async Task Seed_TwoLevelBnvShape_HoldsInvariants_OnTheUntestedRegime()
    {
        // BNV post-merger: 1 root + 22 second-level units, no L3/L4 — the shape the weighted-fill and
        // Pass-3-pin-to-L2 were never originally run against.
        var tenantId = Guid.NewGuid();
        // 210 users over 1 + 22 units does not divide evenly: the per-level split alone seeds 200.
        var profile = MakeProfile(tenantId, userCount: 210);
        var tree = BuildTree(tenantId, (1, 1), (2, 22)); // 23 units

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();

        try
        {
            var total = await TenantUserSeeder.SeedUsersForProfileAsync(
                ctx, hasher, profile, Opts, tree, "DevAdmin@12345", NullLogger.Instance, default);

            (await ctx.Users.CountAsync(u => u.TenantId == tenantId && u.UserName!.StartsWith(profile.LeadEmailPrefix)))
                .Should().Be(23, "one lead per unit holds on a 2-level tree");
            (await ctx.Users.CountAsync(u => u.TenantId == tenantId && u.UserName!.StartsWith(profile.DevEmailPrefix)))
                .Should().Be(Opts.DevReserveCount, "the L2 tier exists so the dev reserve still pins");
            // The remainder top-up must land exactly on target even when the split truncates.
            total.Should().Be(profile.UserCount);
            (await ctx.Users.CountAsync(u => u.TenantId == tenantId)).Should().Be(profile.UserCount);
        }
        finally { await CleanupAsync(ctx, tenantId); }
    }

    [Fact]
    public async Task Seed_RunTwice_ConvergesToStableState()
    {
        // The first run lands exactly on UserCount (remainder top-up), so the second run must take the
        // "users at target" gate and leave every row untouched, instead of the drift path that wipes and
        // re-seeds the tenant (same total, but new user ids on every restart).
        var tenantId = Guid.NewGuid();
        var profile = MakeProfile(tenantId, userCount: 150);
        var tree = BuildTree(tenantId, (1, 1), (2, 8), (3, 16)); // 25 units

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();

        try
        {
            var first = await TenantUserSeeder.SeedUsersForProfileAsync(
                ctx, hasher, profile, Opts, tree, "DevAdmin@12345", NullLogger.Instance, default);
            var leadEmails1 = await LeadEmailsAsync(ctx, tenantId, profile);
            var fakerNames1 = await OrderedFullNamesAsync(ctx, tenantId);
            var userIds1 = await ctx.Users.Where(u => u.TenantId == tenantId).Select(u => u.Id).OrderBy(id => id).ToListAsync();

            var second = await TenantUserSeeder.SeedUsersForProfileAsync(
                ctx, hasher, profile, Opts, tree, "DevAdmin@12345", NullLogger.Instance, default);
            var leadEmails2 = await LeadEmailsAsync(ctx, tenantId, profile);
            var fakerNames2 = await OrderedFullNamesAsync(ctx, tenantId);

            first.Should().Be(profile.UserCount, "the remainder top-up lands the tenant exactly on target");
            second.Should().Be(first, "re-running converges to the same user count");
            (await ctx.Users.Where(u => u.TenantId == tenantId).Select(u => u.Id).OrderBy(id => id).ToListAsync())
                .Should().Equal(userIds1, "a tenant at target is skipped, not wiped and re-seeded");
            leadEmails2.Should().BeEquivalentTo(leadEmails1, "deterministic lead emails are stable across runs");
            (await ctx.Users.CountAsync(u => u.TenantId == tenantId && u.UserName!.StartsWith(profile.LeadEmailPrefix)))
                .Should().Be(tree.Count, "no duplicate leads after a second pass");

            fakerNames2.Should().Equal(fakerNames1, "an at-target tenant keeps its rows");

            // Force the drift path: drop one seeded user so the tenant is below target, then re-run.
            // Faker output is reproducible per call (the per-profile UseSeed isolation): the wipe+reseed
            // regenerates byte-identical FullNames. Were the faker seeded once outside the per-profile
            // scope, the re-seed would continue the sequence and these would diverge.
            await ctx.Users.Where(u => u.TenantId == tenantId && u.UserName!.StartsWith(profile.FillEmailPrefix))
                .OrderBy(u => u.UserName).Take(1).ExecuteDeleteAsync();
            var third = await TenantUserSeeder.SeedUsersForProfileAsync(
                ctx, hasher, profile, Opts, tree, "DevAdmin@12345", NullLogger.Instance, default);
            third.Should().Be(profile.UserCount, "the drift re-seed lands back on target");
            (await OrderedFullNamesAsync(ctx, tenantId)).Should().Equal(fakerNames1,
                "per-call faker re-seeding keeps each tenant's generated names reproducible regardless of run order");
        }
        finally { await CleanupAsync(ctx, tenantId); }
    }

    [Fact]
    public async Task Seed_TwoTenants_NoEmailCollision_EachReachesItsOwnCount()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var profileA = MakeProfile(tenantA, userCount: 120);
        var profileB = MakeProfile(tenantB, userCount: 120);
        var treeA = BuildTree(tenantA, (1, 1), (2, 6));
        var treeB = BuildTree(tenantB, (1, 1), (2, 6));

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();

        try
        {
            await TenantUserSeeder.SeedUsersForProfileAsync(ctx, hasher, profileA, Opts, treeA, "DevAdmin@12345", NullLogger.Instance, default);
            await TenantUserSeeder.SeedUsersForProfileAsync(ctx, hasher, profileB, Opts, treeB, "DevAdmin@12345", NullLogger.Instance, default);

            var emailsA = await ctx.Users.Where(u => u.TenantId == tenantA).Select(u => u.Email!).ToListAsync();
            var emailsB = await ctx.Users.Where(u => u.TenantId == tenantB).Select(u => u.Email!).ToListAsync();

            emailsA.Should().OnlyContain(e => e.EndsWith(profileA.EmailDomain));
            emailsB.Should().OnlyContain(e => e.EndsWith(profileB.EmailDomain));
            emailsA.Intersect(emailsB).Should().BeEmpty("distinct per-tenant email domains prevent any collision");
        }
        finally { await CleanupAsync(ctx, tenantA); await CleanupAsync(ctx, tenantB); }
    }

    [Fact]
    public async Task SeedUserRoles_BindsByPositionPrefix_AndIsResumeSafe()
    {
        var tenantId = Guid.NewGuid();
        var profile = MakeProfile(tenantId, userCount: 100);
        var tree = BuildTree(tenantId, (1, 1), (2, 5));

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        var normalizer = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>();

        try
        {
            // Roles must exist first (the role seeder runs before the user seeder at runtime).
            await TenantRoleSeeder.SeedRolesAsync(ctx, normalizer, tenantId, profile.RolePrefix, NullLogger.Instance, default);
            await TenantUserSeeder.SeedUsersForProfileAsync(ctx, hasher, profile, Opts, tree, "DevAdmin@12345", NullLogger.Instance, default);

            var inserted = await TenantUserSeeder.SeedUserRolesForProfileAsync(ctx, profile, Opts.BatchSize, NullLogger.Instance, default);
            inserted.Should().BeGreaterThan(0);

            // Every seeded user is bound to exactly one role.
            var userIds = await ctx.Users.Where(u => u.TenantId == tenantId).Select(u => u.Id).ToListAsync();
            var bound = await ctx.UserRoles.Where(ur => userIds.Contains(ur.UserId)).CountAsync();
            bound.Should().Be(userIds.Count, "each seeded user gets one position-derived role");

            // The L1 root lead is a MINISTER → bound to {prefix}MINISTER (tenant-scoped role code).
            var rootCode = tree.Single(o => o.Level == 1).Code;
            var rootLeadEmail = $"{profile.LeadEmailPrefix}{rootCode.ToLowerInvariant().Replace('.', '-')}{profile.EmailDomain}";
            var ministerRoleId = await ctx.Roles.Where(r => r.Code == profile.RolePrefix + "MINISTER").Select(r => r.Id).SingleAsync();
            var rootUserId = await ctx.Users.Where(u => u.UserName == rootLeadEmail).Select(u => u.Id).SingleAsync();
            (await ctx.UserRoles.AnyAsync(ur => ur.UserId == rootUserId && ur.RoleId == ministerRoleId))
                .Should().BeTrue("position MINISTER maps to {prefix}MINISTER");

            // Resume-safe: a second bind inserts nothing.
            var again = await TenantUserSeeder.SeedUserRolesForProfileAsync(ctx, profile, Opts.BatchSize, NullLogger.Instance, default);
            again.Should().Be(0, "users already bound are skipped");
        }
        finally
        {
            await CleanupAsync(ctx, tenantId);
            await ctx.Roles.Where(r => r.TenantId == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Seed_UserCountBelowMinimum_ThrowsFailFast()
    {
        var tenantId = Guid.NewGuid();
        // 30 units + 20 dev reserve = 50 minimum, but UserCount=40 < 50.
        var profile = MakeProfile(tenantId, userCount: 40);
        var tree = BuildTree(tenantId, (1, 1), (2, 29)); // 30 units

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();

        try
        {
            var act = async () => await TenantUserSeeder.SeedUsersForProfileAsync(
                ctx, hasher, profile, Opts, tree, "DevAdmin@12345", NullLogger.Instance, default);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*below minimum*");
        }
        finally { await CleanupAsync(ctx, tenantId); }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Builds a synthetic profile whose identity literals are tagged with the tenant id so
    /// emails / codes / role codes are globally unique (shared tables, no per-test rollback).</summary>
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

    /// <summary>Builds a synthetic org tree: each (level, count) tuple yields that many units with
    /// unique tag-prefixed codes. PrimaryOrgUnitId has no FK, so synthetic Guids are fine.</summary>
    private static List<TenantUserSeeder.OrgUnitRow> BuildTree(Guid tenantId, params (int Level, int Count)[] tiers)
    {
        var tag = tenantId.ToString("N")[..8];
        var rows = new List<TenantUserSeeder.OrgUnitRow>();
        foreach (var (level, count) in tiers)
            for (int i = 0; i < count; i++)
                rows.Add(new TenantUserSeeder.OrgUnitRow
                {
                    Id = Guid.NewGuid(),
                    Code = $"{tag}.L{level}.{i:D3}",
                    Level = level,
                });
        return rows;
    }

    private static Task<List<string>> LeadEmailsAsync(IdentityDbContext ctx, Guid tenantId, TenantSeedProfile profile) =>
        ctx.Users.Where(u => u.TenantId == tenantId && u.UserName!.StartsWith(profile.LeadEmailPrefix))
                 .Select(u => u.UserName!).ToListAsync();

    /// <summary>FullNames ordered by the seq-based EmployeeCode — stable key for comparing faker output
    /// across runs (codes regenerate identically since seq order is deterministic).</summary>
    private static Task<List<string>> OrderedFullNamesAsync(IdentityDbContext ctx, Guid tenantId) =>
        ctx.Users.Where(u => u.TenantId == tenantId).OrderBy(u => u.EmployeeCode)
                 .Select(u => u.FullName!).ToListAsync();

    private static async Task CleanupAsync(IdentityDbContext ctx, Guid tenantId)
    {
        var userIds = ctx.Users.Where(u => u.TenantId == tenantId).Select(u => u.Id);
        await ctx.UserRoles.Where(ur => userIds.Contains(ur.UserId)).ExecuteDeleteAsync();
        await ctx.Users.Where(u => u.TenantId == tenantId).ExecuteDeleteAsync();
    }
}
