using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// RT2-15 Phase 02 — real-DB integration tests for IIdentityModuleClient group methods.
///
/// Locks two contracts against a Testcontainers SQL Server DB:
///   1. ResolveActiveUsersInGroupsAsync — 3-table JOIN filtering inactive groups + inactive users.
///   2. ResolveActiveUserGroupsByCodesAsync — single-table Code subset filter (Save-time Q2).
///
/// Each test seeds unique codes/users/groups to avoid cross-test contention on the shared DB.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class IdentityModuleClientUserGroupsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public IdentityModuleClientUserGroupsTests(WebAppFixture app)
    {
        _app = app;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ResolveActiveUsersInGroupsAsync integration tests
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Happy path: active group + active user → user ID returned.
    /// Inactive group with same code query → filtered out.
    /// Inactive user in active group → filtered out.
    /// All three scenarios in one seed for minimal Testcontainers overhead.
    /// </summary>
    [Fact]
    public async Task ResolveActiveUsersInGroupsAsync_Integration_FiltersByActiveGroupAndUser()
    {
        // Unique codes prevent collisions on shared Testcontainers DB.
        var activeCode = $"GRP_ACTIVE_{Guid.NewGuid():N}".ToUpperInvariant();
        var inactiveGroupCode = $"GRP_INACTIVE_{Guid.NewGuid():N}".ToUpperInvariant();

        // Pre-assign IDs outside the lambda so they're accessible in both the seed and assertion scopes.
        var activeUserId = Guid.NewGuid();
        var inactiveUserId = Guid.NewGuid();
        var inactiveGroupUserId = Guid.NewGuid();

        await WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();

            // Active group with 2 members: one active user, one inactive user.
            var activeGroupId = await InsertGroupRawAsync(db, activeCode, isActive: true);

            db.Users.Add(BuildUser(activeUserId, isActive: true));
            db.Users.Add(BuildUser(inactiveUserId, isActive: false));
            await db.SaveChangesAsync();

            db.Set<UserGroupMembership>().Add(new UserGroupMembership
            {
                Id = Guid.NewGuid(), UserId = activeUserId, GroupId = activeGroupId,
                AddedAtUtc = DateTime.UtcNow, AddedBy = Guid.NewGuid(),
            });
            db.Set<UserGroupMembership>().Add(new UserGroupMembership
            {
                Id = Guid.NewGuid(), UserId = inactiveUserId, GroupId = activeGroupId,
                AddedAtUtc = DateTime.UtcNow, AddedBy = Guid.NewGuid(),
            });

            // Inactive group with 1 active user — should not surface.
            var inactiveGroupId = await InsertGroupRawAsync(db, inactiveGroupCode, isActive: false);
            db.Users.Add(BuildUser(inactiveGroupUserId, isActive: true));
            await db.SaveChangesAsync();

            db.Set<UserGroupMembership>().Add(new UserGroupMembership
            {
                Id = Guid.NewGuid(), UserId = inactiveGroupUserId, GroupId = inactiveGroupId,
                AddedAtUtc = DateTime.UtcNow, AddedBy = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
            return true;
        });

        // Query both codes — only activeUserId from activeCode should surface.
        // TenantId=Guid.Empty: both inserted groups have TenantId=NULL (global) so they match any tenant.
        var queryTenantId = Guid.NewGuid();
        var result = await ExecAsync(c =>
            c.ResolveActiveUsersInGroupsAsync([activeCode, inactiveGroupCode], queryTenantId));

        result.Should().ContainSingle(
            "only the active-user in the active-group must surface");
        result.Should().NotContain(inactiveUserId,
            "inactive user in active group must be filtered out (u.IsActive=1 filter)");
        result.Should().NotContain(inactiveGroupUserId,
            "user in inactive group must be filtered out (g.IsActive=1 filter)");
        result[0].Should().Be(activeUserId,
            "the single returned ID must be the active user in the active group");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ResolveActiveUserGroupsByCodesAsync integration tests
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Anti-tautology (260522-E): input has 3 codes — one active group, one inactive group,
    /// one non-existent. Asserts ONLY the active group code is returned, and that neither
    /// the inactive nor the non-existent pollutes the result set.
    /// </summary>
    [Fact]
    public async Task ResolveActiveUserGroupsByCodesAsync_Integration_ReturnsCanonicalCodeSubset()
    {
        var knownActiveCode = $"GRP_KNOWN_{Guid.NewGuid():N}".ToUpperInvariant();
        var knownInactiveCode = $"GRP_INACT_{Guid.NewGuid():N}".ToUpperInvariant();
        var unknownCode = $"GRP_GHOST_{Guid.NewGuid():N}".ToUpperInvariant();

        await WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            await InsertGroupRawAsync(db, knownActiveCode, isActive: true);
            await InsertGroupRawAsync(db, knownInactiveCode, isActive: false);
            return true;
        });

        // TenantId=Guid.NewGuid(): all inserted groups have TenantId=NULL (global), so they match any tenant.
        var queryTenantId = Guid.NewGuid();
        var result = await ExecAsync(c =>
            c.ResolveActiveUserGroupsByCodesAsync([knownActiveCode, knownInactiveCode, unknownCode], queryTenantId));

        result.Should().Contain(knownActiveCode,
            "active group code must be in the returned subset");
        result.Should().NotContain(knownInactiveCode,
            "inactive group code must NOT appear — IsActive=1 filter");
        result.Should().NotContain(unknownCode,
            "non-existent code must NOT appear (caller detects via set-difference)");
        result.Should().HaveCount(1,
            "only the single active known code must be returned");

        // Ordinal comparer lock: canonical uppercase only.
        result.Contains(knownActiveCode).Should().BeTrue();
        result.Contains(knownActiveCode.ToLowerInvariant()).Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Cross-tenant isolation tests (C1 fix verification)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// I_CT1 — proves the C1 fix: GROUP_FINANCE in tenant A must NOT be returned when querying tenant B.
    /// Seeds the same Code ("GROUP_FINANCE_CT1_*") in two distinct tenants plus a global (NULL-tenant) group.
    /// Asserts that querying tenant A returns only tenant-A's row + global; querying tenant B returns only tenant-B's row + global.
    /// Without the (TenantId=@TenantId OR TenantId IS NULL) WHERE predicate, both queries would erroneously
    /// return BOTH tenant rows (cross-tenant data leak caught by C1 code review).
    /// </summary>
    [Fact]
    public async Task I_CT1_ResolveActiveUserGroupsByCodes_CrossTenant_DoesNotLeak()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var financeCode = $"GROUP_FINANCE_CT1_{suffix}";
        var globalCode = $"GROUP_GLOBAL_CT1_{suffix}";

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            // Seed same Code in tenant A and tenant B (distinct rows — (Code, TenantId) unique index allows this).
            await InsertGroupWithTenantRawAsync(db, financeCode, tenantId: tenantA, isActive: true);
            await InsertGroupWithTenantRawAsync(db, financeCode, tenantId: tenantB, isActive: true);
            // Seed a global group (TenantId=NULL) — must surface for BOTH tenant queries.
            await InsertGroupRawAsync(db, globalCode, isActive: true);
            return true;
        });

        // Query tenant A: must see tenant A's GROUP_FINANCE + the global group.
        var resultA = await ExecAsync(c =>
            c.ResolveActiveUserGroupsByCodesAsync([financeCode, globalCode], tenantA));

        resultA.Should().Contain(financeCode,
            "tenant A's GROUP_FINANCE must be visible under tenant A query");
        resultA.Should().Contain(globalCode,
            "global (NULL-tenant) group must be visible under any tenant");
        resultA.Should().HaveCount(2,
            "only 2 distinct groups match under tenant A (tenant-A row + global)");

        // Query tenant B: must see tenant B's GROUP_FINANCE + the global group — same codes, different rows.
        var resultB = await ExecAsync(c =>
            c.ResolveActiveUserGroupsByCodesAsync([financeCode, globalCode], tenantB));

        resultB.Should().Contain(financeCode,
            "tenant B's GROUP_FINANCE must be visible under tenant B query");
        resultB.Should().Contain(globalCode,
            "global group must be visible under tenant B as well");
        resultB.Should().HaveCount(2,
            "tenant B gets its own row + global — not leaking tenant A's row");
    }

    /// <summary>
    /// I_CT1b — proves cross-tenant isolation for the runtime resolver (user resolution).
    /// Seeds GROUP_FINANCE_CT1b in two distinct tenants, adds user A to tenant A's group and user B to tenant B's group.
    /// Asserts that querying tenant A returns ONLY user A; querying tenant B returns ONLY user B.
    /// </summary>
    [Fact]
    public async Task I_CT1b_ResolveActiveUsersInGroups_CrossTenant_DoesNotLeak()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var financeCode = $"GROUP_FINANCE_CT1B_{suffix}";

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();

            // Seed user A and user B — no TenantId on ApplicationUser here (global users for simplicity).
            db.Users.Add(BuildUser(userA, isActive: true));
            db.Users.Add(BuildUser(userB, isActive: true));
            await db.SaveChangesAsync();

            // Seed GROUP_FINANCE in tenant A; add user A.
            var groupAId = await InsertGroupWithTenantRawAsync(db, financeCode, tenantId: tenantA, isActive: true);
            db.Set<UserGroupMembership>().Add(new UserGroupMembership
            {
                Id = Guid.NewGuid(), UserId = userA, GroupId = groupAId,
                AddedAtUtc = DateTime.UtcNow, AddedBy = Guid.NewGuid(),
            });

            // Seed GROUP_FINANCE in tenant B; add user B.
            var groupBId = await InsertGroupWithTenantRawAsync(db, financeCode, tenantId: tenantB, isActive: true);
            db.Set<UserGroupMembership>().Add(new UserGroupMembership
            {
                Id = Guid.NewGuid(), UserId = userB, GroupId = groupBId,
                AddedAtUtc = DateTime.UtcNow, AddedBy = Guid.NewGuid(),
            });

            await db.SaveChangesAsync();
            return true;
        });

        // Query tenant A: must return ONLY user A.
        var resultA = await ExecAsync(c =>
            c.ResolveActiveUsersInGroupsAsync([financeCode], tenantA));

        resultA.Should().ContainSingle(
            "only user A (member of tenant-A's GROUP_FINANCE) must be returned");
        resultA.Should().Contain(userA,
            "user A must appear in tenant A's group resolution");
        resultA.Should().NotContain(userB,
            "user B is in tenant B's group — MUST NOT leak into tenant A's resolution (C1 fix)");

        // Query tenant B: must return ONLY user B.
        var resultB = await ExecAsync(c =>
            c.ResolveActiveUsersInGroupsAsync([financeCode], tenantB));

        resultB.Should().ContainSingle(
            "only user B (member of tenant-B's GROUP_FINANCE) must be returned");
        resultB.Should().Contain(userB,
            "user B must appear in tenant B's group resolution");
        resultB.Should().NotContain(userA,
            "user A is in tenant A's group — MUST NOT leak into tenant B's resolution (C1 fix)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Soft-delete propagation: membership mirrors the principal group's !IsDeleted filter
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// UserGroupMembership has a required FK to UserGroup, which carries a global !IsDeleted
    /// filter. The membership defines a matching filter so that once the group is soft-deleted,
    /// its memberships disappear from normal queries (and the required Group nav never resolves
    /// to null). IgnoreQueryFilters still exposes the row, proving it is filtered — not deleted.
    /// Without the matching filter the post-delete assertion would find the membership still
    /// visible.
    /// </summary>
    [Fact]
    public async Task UserGroupMembership_IsFilteredOut_WhenGroupSoftDeleted()
    {
        var code = $"GRP_SD_{Guid.NewGuid():N}".ToUpperInvariant();
        var userId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        long groupId = 0;

        await WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            groupId = await InsertGroupRawAsync(db, code, isActive: true);
            db.Users.Add(BuildUser(userId, isActive: true));
            await db.SaveChangesAsync();
            db.Set<UserGroupMembership>().Add(new UserGroupMembership
            {
                Id = membershipId, UserId = userId, GroupId = groupId,
                AddedAtUtc = DateTime.UtcNow, AddedBy = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
            return true;
        });

        // Live group → membership visible.
        await WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            (await db.Set<UserGroupMembership>().AnyAsync(m => m.Id == membershipId))
                .Should().BeTrue("a membership of a live group must be visible");
            return true;
        });

        // Soft-delete the group.
        await WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var group = await db.Set<UserGroup>().SingleAsync(g => g.Id == groupId);
            group.Delete();
            await db.SaveChangesAsync();
            return true;
        });

        // Soft-deleted group → membership filtered out, but the row still exists physically.
        await WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            (await db.Set<UserGroupMembership>().AnyAsync(m => m.Id == membershipId))
                .Should().BeFalse("membership of a soft-deleted group must be filtered out");
            (await db.Set<UserGroupMembership>().IgnoreQueryFilters().AnyAsync(m => m.Id == membershipId))
                .Should().BeTrue("the row is only filtered, not physically deleted");
            return true;
        });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private Task<TResult> ExecAsync<TResult>(Func<IIdentityModuleClient, Task<TResult>> action)
        => WithScopeAsync(async sp =>
        {
            var client = sp.GetRequiredService<IIdentityModuleClient>();
            return await action(client);
        });

    private async Task<TResult> WithScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>
    /// Raw INSERT into [identity].[UserGroups] bypassing domain factory (avoids IGuidGenerator
    /// ceremony). Mirrors InsertGroupRawAsync in IdentityModuleClientRoleNamesTests. Returns
    /// the DB-assigned long PK (IDENTITY) for use in membership seeding.
    /// </summary>
    private static async Task<long> InsertGroupRawAsync(IdentityDbContext db, string code, bool isActive)
    {
        var publicId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var activeFlag = isActive ? 1 : 0;

        var sql = $"""
            INSERT INTO [identity].[UserGroups]
                (PublicId, Code, Name, IsActive, TenantId,
                 CreatedAt, CreatedBy, IsDeleted, ClassificationLevel)
            OUTPUT INSERTED.Id
            VALUES
                (@PublicId, @Code, @Code, {activeFlag}, NULL,
                 SYSUTCDATETIME(), @Actor, 0, 2);
            """;

        var connStr = db.Database.GetConnectionString()!;
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@PublicId", publicId);
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@Actor", actor);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Raw INSERT into [identity].[UserGroups] with an explicit TenantId (non-null).
    /// Used for cross-tenant isolation tests that require two rows with the same Code
    /// but different TenantIds — matching the (Code, TenantId) UNIQUE-index semantics.
    /// Returns the DB-assigned long PK (IDENTITY) for membership seeding.
    /// </summary>
    private static async Task<long> InsertGroupWithTenantRawAsync(
        IdentityDbContext db, string code, Guid tenantId, bool isActive)
    {
        var publicId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var activeFlag = isActive ? 1 : 0;

        var sql = $"""
            INSERT INTO [identity].[UserGroups]
                (PublicId, Code, Name, IsActive, TenantId,
                 CreatedAt, CreatedBy, IsDeleted, ClassificationLevel)
            OUTPUT INSERTED.Id
            VALUES
                (@PublicId, @Code, @Code, {activeFlag}, @TenantId,
                 SYSUTCDATETIME(), @Actor, 0, 2);
            """;

        var connStr = db.Database.GetConnectionString()!;
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@PublicId", publicId);
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@Actor", actor);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private static ApplicationUser BuildUser(Guid userId, bool isActive)
    {
        var email = $"grp.test.{userId:N}@example.vn";
        return new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "Group-test user",
            IsActive = isActive,
        };
    }
}
