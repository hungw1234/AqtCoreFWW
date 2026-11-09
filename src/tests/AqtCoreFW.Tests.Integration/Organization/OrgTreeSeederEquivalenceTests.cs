using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Organization;

/// <summary>
/// Output-equivalence net for the new tenant-agnostic <see cref="OrgTreeSeeder"/> against the legacy
/// BtcOrgUnitSeeder's tree. The legacy integration assertions (L1=1 / L2=35 / L3=109 / L4 tax=350 /
/// stats≥476) are skipped in their own project (WebAppFixture/Hangfire race), so this is the LIVE
/// safety net the parallel-change migration needs before the legacy seeder can be deleted.
///
/// Strategy: run the new seeder under a FRESH synthetic tenant with the BTC tree plan (same JSON
/// resources the legacy seeder consumed) and assert it reproduces the BTC structure node-for-node.
/// Fresh tenant + the (TenantId, Code) unique index keep this isolated from the fixture's own BTC
/// rows. Treasury ward-placeholders are excluded (ward-data-dependent, non-deterministic count) —
/// this proves the JSON-driven generalization, which is the part that replaces the legacy methods.
/// </summary>
[Collection("Integration")]
public class OrgTreeSeederEquivalenceTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    /// <summary>System tenant context (mirrors the internal SystemSeederTenantContext): bypasses the
    /// global tenant filter so the seeder can query/insert with an explicit tenant id.</summary>
    private sealed class SystemSeederTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public bool IsSystemAdmin => true;
    }

    private OrgDbContext NewSeederContext(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<DbContextOptions<OrgDbContext>>();
        return new OrgDbContext(options, new SystemSeederTenantContext());
    }

    [Fact]
    public async Task SeedTree_ReproducesBtcStructure_AndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        await using (var ctx = NewSeederContext(scope.ServiceProvider))
        {
            await OrgTreeSeeder.SeedTreeAsync(
                ctx, tenantId, OrgTreePlans.BoTaiChinh, gen,
                maxLevel: 4, skipTreasuryWardPlaceholders: true,
                NullLogger.Instance, CancellationToken.None);
        }

        List<OrgUnit> tree;
        await using (var ctx = NewSeederContext(scope.ServiceProvider))
        {
            tree = await ctx.OrgUnits.IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId)
                .ToListAsync();
        }

        // ── Level counts match the canonical BTC tree ──────────────────────────
        var l1 = tree.Where(o => o.Level == 1).ToList();
        l1.Should().ContainSingle(o => o.Code == "BTC", "exactly one BTC root at L1");
        l1.Single().ParentId.Should().BeNull("the root has no parent");

        tree.Count(o => o.Level == 2).Should().Be(35, "35 central units (Vụ/Cục/đơn vị)");
        tree.Count(o => o.Level == 3).Should().Be(109, "20 tax + 20 customs + 20 treasury + 15 reserve + 34 statistics");
        tree.Count(o => o.Level == 4 && o.Code.StartsWith("BTC.C.THUE.")).Should().Be(350, "350 đội thuế");
        tree.Count(o => o.Level == 4 && o.Code.StartsWith("BTC.C.TK.")).Should().BeGreaterThanOrEqualTo(476, "≥476 statistics teams (BTC.C.TK.<region>.<team>)");
        tree.Should().NotContain(o => o.Code == "_META", "the statistics JSON _META sentinel row must never become a node");

        // ── Structural integrity: no orphans, parents one level up ─────────────
        var idByKey = tree.ToDictionary(o => o.Id);
        var rootId = l1.Single().Id;
        tree.Where(o => o.Level == 2).Should().OnlyContain(o => o.ParentId == rootId,
            "every L2 unit hangs off the BTC root");
        foreach (var node in tree.Where(o => o.Level >= 3))
        {
            node.ParentId.Should().NotBeNull($"{node.Code} (L{node.Level}) must have a parent");
            idByKey.Should().ContainKey(node.ParentId!.Value,
                $"{node.Code} parent must be within the same tenant's tree");
            idByKey[node.ParentId!.Value].Level.Should().Be(node.Level - 1,
                $"{node.Code} parent must be exactly one level up");
        }

        var totalAfterFirst = tree.Count;

        // ── Idempotency: a second pass inserts nothing ─────────────────────────
        await using (var ctx = NewSeederContext(scope.ServiceProvider))
        {
            await OrgTreeSeeder.SeedTreeAsync(
                ctx, tenantId, OrgTreePlans.BoTaiChinh, gen,
                maxLevel: 4, skipTreasuryWardPlaceholders: true,
                NullLogger.Instance, CancellationToken.None);
        }

        int totalAfterSecond;
        await using (var ctx = NewSeederContext(scope.ServiceProvider))
        {
            totalAfterSecond = await ctx.OrgUnits.IgnoreQueryFilters()
                .CountAsync(o => o.TenantId == tenantId);
        }

        totalAfterSecond.Should().Be(totalAfterFirst, "re-running the seeder must not duplicate nodes");
    }

    /// <summary>
    /// The same generic seeder drives a second tenant from data alone: Bộ Nội Vụ is a flat 2-level
    /// tree (1 root + 22 second-level units, no treasury, no level-3+). This proves the seeder is
    /// genuinely tenant-agnostic — a new tenant needs only a JSON resource + an OrgTreePlan entry,
    /// no code path of its own — and that Vietnamese diacritics survive the round-trip.
    /// </summary>
    [Fact]
    public async Task SeedTree_BoNoiVu_Produces1RootAnd22Level2Units()
    {
        var tenantId = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        await using (var ctx = NewSeederContext(scope.ServiceProvider))
        {
            await OrgTreeSeeder.SeedTreeAsync(
                ctx, tenantId, OrgTreePlans.BoNoiVu, gen,
                maxLevel: 2, skipTreasuryWardPlaceholders: true,
                NullLogger.Instance, CancellationToken.None);
        }

        List<OrgUnit> tree;
        await using (var ctx = NewSeederContext(scope.ServiceProvider))
        {
            tree = await ctx.OrgUnits.IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId)
                .ToListAsync();
        }

        // ── Shape: 1 root + 22 second-level, nothing deeper ────────────────────
        var l1 = tree.Where(o => o.Level == 1).ToList();
        l1.Should().ContainSingle(o => o.Code == "BNV", "exactly one BNV root at L1");
        l1.Single().ParentId.Should().BeNull("the root has no parent");

        var rootId = l1.Single().Id;
        var l2 = tree.Where(o => o.Level == 2).ToList();
        l2.Should().HaveCount(22, "10 Vụ + 5 Cục + 3 Ban/Thanh tra/Văn phòng + 4 sự nghiệp");
        l2.Should().OnlyContain(o => o.ParentId == rootId, "every BNV unit hangs off the BNV root");
        tree.Should().NotContain(o => o.Level >= 3, "BNV has no level-3+ data (MaxLevel=2)");

        // ── Diacritics survive the full insert→read round-trip ─────────────────
        tree.Should().Contain(o => o.Name == "Vụ Tổ chức - Biên chế",
            "Vietnamese diacritics must be stored and read back byte-for-byte");
        tree.Should().Contain(o => o.Name == "Báo Dân trí");

        var totalAfterFirst = tree.Count;

        // ── Idempotency: a second pass inserts nothing ─────────────────────────
        await using (var ctx = NewSeederContext(scope.ServiceProvider))
        {
            await OrgTreeSeeder.SeedTreeAsync(
                ctx, tenantId, OrgTreePlans.BoNoiVu, gen,
                maxLevel: 2, skipTreasuryWardPlaceholders: true,
                NullLogger.Instance, CancellationToken.None);
        }

        int totalAfterSecond;
        await using (var ctx = NewSeederContext(scope.ServiceProvider))
        {
            totalAfterSecond = await ctx.OrgUnits.IgnoreQueryFilters()
                .CountAsync(o => o.TenantId == tenantId);
        }

        totalAfterSecond.Should().Be(totalAfterFirst, "re-running the seeder must not duplicate nodes");
    }

    /// <summary>
    /// Covers the BTC-only treasury step (the L4 KBNN placeholder generation that reads
    /// masterdata.Wards via Dapper) — the one branch the equivalence test skips. Seeds a few
    /// uniquely-coded test wards (one flipped inactive), runs the full BTC seed WITH the treasury
    /// step, and asserts a placeholder is produced per ACTIVE ward (inactive filtered out),
    /// parented under a KBNN khu vực, idempotently. Assertions are scoped to the test wards' codes
    /// so they stay deterministic regardless of the wards MasterDataSeeder already seeded.
    /// </summary>
    [Fact]
    public async Task SeedTree_GeneratesTreasuryWardPlaceholders_FromActiveWards()
    {
        // Code is max-10 + globally unique; the "ZZ" prefix avoids colliding with GSO-numeric codes.
        const string activeA = "ZZW001";
        const string activeB = "ZZW002";
        const string inactiveC = "ZZW003";

        var tenantId = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var md = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();

        try
        {
            md.Wards.AddRange(
                Ward.Create(activeA, "ZZD001", "Phường Thử Nghiệm Một", "Test Ward One"),
                Ward.Create(activeB, "ZZD001", "Phường Thử Nghiệm Hai", "Test Ward Two"),
                Ward.Create(inactiveC, "ZZD001", "Phường Thử Nghiệm Ba", "Test Ward Three"));
            await md.SaveChangesAsync();
            // Ward.Create always sets IsActive=true; flip one to exercise the WHERE IsActive=1 filter.
            await md.Database.ExecuteSqlRawAsync(
                "UPDATE masterdata.Wards SET IsActive = 0 WHERE Code = {0}", inactiveC);

            await using (var ctx = NewSeederContext(scope.ServiceProvider))
            {
                await OrgTreeSeeder.SeedTreeAsync(
                    ctx, tenantId, OrgTreePlans.BoTaiChinh, gen,
                    maxLevel: 4, skipTreasuryWardPlaceholders: false,
                    NullLogger.Instance, CancellationToken.None);
            }

            List<OrgUnit> tree;
            await using (var ctx = NewSeederContext(scope.ServiceProvider))
            {
                tree = await ctx.OrgUnits.IgnoreQueryFilters()
                    .Where(o => o.TenantId == tenantId)
                    .ToListAsync();
            }
            var byCode = tree.ToDictionary(o => o.Code);
            var idToNode = tree.ToDictionary(o => o.Id);

            // ── A placeholder per active ward; the inactive ward is filtered out ───
            byCode.Should().ContainKey($"BTC.KBNN.PLACEHOLDER.{activeA}");
            byCode.Should().ContainKey($"BTC.KBNN.PLACEHOLDER.{activeB}");
            byCode.Should().NotContainKey($"BTC.KBNN.PLACEHOLDER.{inactiveC}",
                "wards with IsActive=0 must be filtered out of the placeholder seed");

            // ── Shape: L4, degenerate KBNN parent, ward name embedded verbatim ────
            var placeholderA = byCode[$"BTC.KBNN.PLACEHOLDER.{activeA}"];
            placeholderA.Level.Should().Be(4);
            placeholderA.Name.Should().Be("KBNN Phòng Giao dịch Phường Thử Nghiệm Một",
                "the placeholder name embeds the ward's Vietnamese name verbatim");
            placeholderA.ParentId.Should().NotBeNull();
            var parent = idToNode[placeholderA.ParentId!.Value];
            parent.Code.Should().StartWith("BTC.KBNN.", "degenerate parent is the first KBNN khu vực");
            parent.Level.Should().Be(3);

            tree.Count(o => o.Code.StartsWith("BTC.KBNN.PLACEHOLDER.ZZW"))
                .Should().Be(2, "exactly the two active test wards produced placeholders");

            // ── Idempotency: a second treasury pass inserts no new placeholders ───
            await using (var ctx = NewSeederContext(scope.ServiceProvider))
            {
                await OrgTreeSeeder.SeedTreeAsync(
                    ctx, tenantId, OrgTreePlans.BoTaiChinh, gen,
                    maxLevel: 4, skipTreasuryWardPlaceholders: false,
                    NullLogger.Instance, CancellationToken.None);
            }

            int countAfterSecond;
            await using (var ctx = NewSeederContext(scope.ServiceProvider))
            {
                countAfterSecond = await ctx.OrgUnits.IgnoreQueryFilters()
                    .CountAsync(o => o.TenantId == tenantId && o.Code.StartsWith("BTC.KBNN.PLACEHOLDER.ZZW"));
            }
            countAfterSecond.Should().Be(2, "re-running the treasury step must not duplicate placeholders");
        }
        finally
        {
            // masterdata.Wards is global (shared fixture, no rollback) — remove the test wards so
            // other treasury-seeding runs are unaffected. Placeholder OrgUnits live under a throwaway
            // tenant id, so they need no cleanup.
            await md.Database.ExecuteSqlRawAsync(
                "DELETE FROM masterdata.Wards WHERE Code IN ({0}, {1}, {2})",
                activeA, activeB, inactiveC);
        }
    }
}
