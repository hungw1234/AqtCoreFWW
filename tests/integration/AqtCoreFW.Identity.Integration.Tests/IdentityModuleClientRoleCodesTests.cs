using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Real-DB integration tests for <see cref="IIdentityModuleClient.GetUserRoleCodesAsync"/>.
///
/// ByRole matching keys on role CODE (stable ASCII, e.g. "BTC_MINISTER") — consistent with the
/// Permission/Group/OrgUnit code semantics. Exercises the real Dapper SQL against a Testcontainers
/// SQL Server DB to lock the PQ-1 A direct-only semantic:
///   1. Direct AspNetUserRoles row → role code returned.
///   2. Role assigned ONLY via UserGroupMembership → GroupRoleAssignment → code NOT returned
///      (anti-tautology lock for memory invariant 260524-G — privilege-escalation trap).
///   3. AspNetRoles.IsActive = false → code NOT returned.
///   4. Tenant scope: global (TenantId IS NULL) ∪ matching TenantId → returned; cross-tenant excluded.
///   5. Canonical UPPER+TRIM transformation against role with whitespace+lowercase code.
///
/// Each test uses unique tenant + role + user IDs to avoid Testcontainers cross-test contention.
/// NormalizedName is UNIQUE → role Name is globally unique per test; Code is the matched value.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class IdentityModuleClientRoleCodesTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public IdentityModuleClientRoleCodesTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task GetUserRoleCodesAsync_UserWithDirectRole_ReturnsRoleCode()
    {
        var tenantId = Guid.NewGuid();
        var roleCode = $"REVIEWER_{Guid.NewGuid():N}";
        var (userId, canonical) = await SeedUserWithDirectRoleAsync(tenantId, roleCode);

        var roleCodes = await ExecAsync(c => c.GetUserRoleCodesAsync(userId, tenantId));

        roleCodes.Should().Contain(canonical,
            "direct AspNetUserRoles entry must surface in the role-code set");
    }

    /// <summary>
    /// Anti-tautology lock for memory invariant 260524-G + 260523-D fail-CLOSED-with-reader.
    /// If a future refactor merges <c>UserGroupMembership → GroupRoleAssignment</c> (e.g. by
    /// proxying through <c>IEffectivePermissionService</c>), this test catches the
    /// privilege-escalation silently introduced by that change.
    /// </summary>
    [Fact]
    public async Task GetUserRoleCodesAsync_UserWithRoleViaGroupOnly_ExcludesRoleCode()
    {
        var tenantId = Guid.NewGuid();
        var roleCode = $"APPROVER_{Guid.NewGuid():N}";
        var (userId, viaGroupRoleCode) = await SeedUserWithGroupOnlyRoleAsync(tenantId, roleCode);

        var roleCodes = await ExecAsync(c => c.GetUserRoleCodesAsync(userId, tenantId));

        roleCodes.Should().NotContain(viaGroupRoleCode,
            "PQ-1 A direct-only: roles inherited via UserGroupMembership → GroupRoleAssignment " +
            "MUST be excluded (no privilege escalation through group cascade)");
    }

    [Fact]
    public async Task GetUserRoleCodesAsync_InactiveRole_Excluded()
    {
        var tenantId = Guid.NewGuid();
        var roleCode = $"AUDITOR_{Guid.NewGuid():N}";
        var (userId, inactiveRoleCode) = await SeedUserWithDirectRoleAsync(
            tenantId, roleCode, isActive: false);

        var roleCodes = await ExecAsync(c => c.GetUserRoleCodesAsync(userId, tenantId));

        roleCodes.Should().NotContain(inactiveRoleCode,
            "ApplicationRole.IsActive=false MUST exclude the role code (soft-disable honored)");
    }

    /// <summary>
    /// Tenant scope: global role (TenantId=NULL) returned for ANY tenant; tenant-A role
    /// returned only when query passes TenantId=A; tenant-B role excluded entirely from
    /// a TenantId=A query.
    /// </summary>
    [Fact]
    public async Task GetUserRoleCodesAsync_TenantScope_IncludesGlobalAndMatching_ExcludesOther()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Canonicalized (UPPER) to match SQL UPPER(LTRIM(RTRIM(r.Code))) output exactly.
        var globalCode = $"GLOBAL_{Guid.NewGuid():N}".ToUpperInvariant();
        var tenantACode = $"TENANT_A_{Guid.NewGuid():N}".ToUpperInvariant();
        var tenantBCode = $"TENANT_B_{Guid.NewGuid():N}".ToUpperInvariant();
        var globalRole = await SeedRoleAsync(tenantId: null, code: globalCode);
        var tenantARole = await SeedRoleAsync(tenantId: tenantA, code: tenantACode);
        var tenantBRole = await SeedRoleAsync(tenantId: tenantB, code: tenantBCode);

        await SeedUserAsync(userId);
        await BindUserRolesAsync(userId, globalRole.Id, tenantARole.Id, tenantBRole.Id);

        var roleCodes = await ExecAsync(c => c.GetUserRoleCodesAsync(userId, tenantA));

        roleCodes.Should().Contain(globalCode,
            "global role (TenantId IS NULL) must be visible regardless of caller's tenant");
        roleCodes.Should().Contain(tenantACode,
            "role for caller's tenant must be visible");
        roleCodes.Should().NotContain(tenantBCode,
            "role for different tenant must NOT be visible (cross-tenant defense)");
    }

    /// <summary>
    /// SQL <c>UPPER(LTRIM(RTRIM(r.Code)))</c> canonicalization mirrors
    /// <c>WorkflowGraphValidator</c> Save-time canonicalization. Without LTRIM/RTRIM a code with
    /// leading/trailing whitespace would not match resolver-pushed candidate-index refs.
    /// </summary>
    [Fact]
    public async Task GetUserRoleCodesAsync_RoleCodeWithWhitespaceAndCase_ReturnsCanonicalUpperTrim()
    {
        var tenantId = Guid.NewGuid();
        // Insert intentionally messy code; SQL must produce the canonical form.
        var core = $"reviewer_{Guid.NewGuid():N}";
        var rawCode = $"  {core}  ";
        var canonical = core.Trim().ToUpperInvariant();
        var (userId, _) = await SeedUserWithDirectRoleAsync(tenantId, roleCode: rawCode);

        var roleCodes = await ExecAsync(c => c.GetUserRoleCodesAsync(userId, tenantId));

        roleCodes.Should().Contain(canonical,
            "SQL UPPER(LTRIM(RTRIM(r.Code))) must produce canonical form matching " +
            "WorkflowGraphValidator Save-time canonicalization");
        roleCodes.Should().NotContain(rawCode,
            "non-canonical form must not be surfaced");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers — direct DbContext seeding (no UserManager — keeps test isolated from
    // password validators, normalizer state, and other Identity ceremony).
    // ─────────────────────────────────────────────────────────────────────

    private Task<TResult> ExecAsync<TResult>(Func<IIdentityModuleClient, Task<TResult>> action)
    {
        return WithScopeAsync(async sp =>
        {
            var client = sp.GetRequiredService<IIdentityModuleClient>();
            return await action(client);
        });
    }

    private async Task<TResult> WithScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<(Guid UserId, string CanonicalRoleCode)> SeedUserWithDirectRoleAsync(
        Guid tenantId, string roleCode, bool isActive = true)
    {
        return WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var userId = Guid.NewGuid();

            var role = BuildRole(code: roleCode, tenantId: tenantId, isActive: isActive);
            db.Roles.Add(role);

            var user = BuildUser(userId);
            db.Users.Add(user);

            await db.SaveChangesAsync();

            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = role.Id });
            await db.SaveChangesAsync();

            return (userId, roleCode.Trim().ToUpperInvariant());
        });
    }

    private Task<(Guid UserId, string CanonicalRoleCode)> SeedUserWithGroupOnlyRoleAsync(
        Guid tenantId, string roleCode)
    {
        return WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var userId = Guid.NewGuid();

            var role = BuildRole(code: roleCode, tenantId: tenantId, isActive: true);
            db.Roles.Add(role);

            var user = BuildUser(userId);
            db.Users.Add(user);

            await db.SaveChangesAsync();

            // Build group + membership + group-role-assignment WITHOUT a direct AspNetUserRoles row.
            // This is the exact pattern PQ-1 A direct-only is designed to exclude.
            var groupId = await InsertGroupRawAsync(db, tenantId);

            db.Set<UserGroupMembership>().Add(new UserGroupMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GroupId = groupId,
                AddedAtUtc = DateTime.UtcNow,
                AddedBy = Guid.NewGuid(),
            });
            db.Set<GroupRoleAssignment>().Add(new GroupRoleAssignment
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                RoleId = role.Id,
                AssignedAtUtc = DateTime.UtcNow,
                AssignedBy = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();

            return (userId, roleCode.Trim().ToUpperInvariant());
        });
    }

    private async Task<long> InsertGroupRawAsync(IdentityDbContext db, Guid tenantId)
    {
        // SEC_Group: minimal required cols. PublicId Guid, Code unique-per-tenant, Name, IsActive,
        // TenantId, AuditableEntity columns (CreatedAt/CreatedBy/IsDeleted).
        var publicId = Guid.NewGuid();
        var code = $"TEST_GROUP_{Guid.NewGuid():N}";
        var actor = Guid.NewGuid();

        const string tableName = "UserGroups";
        // ClassificationLevel=Internal(2) per QĐ742 enum default — column is NOT NULL.
        var sql = $"""
            INSERT INTO [identity].[{tableName}]
                (PublicId, Code, Name, IsActive, TenantId,
                 CreatedAt, CreatedBy, IsDeleted, ClassificationLevel)
            OUTPUT INSERTED.Id
            VALUES
                (@PublicId, @Code, @Code, 1, @TenantId,
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

    private Task<ApplicationRole> SeedRoleAsync(Guid? tenantId, string code)
    {
        return WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var role = BuildRole(code: code, tenantId: tenantId, isActive: true);
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            return role;
        });
    }

    private Task SeedUserAsync(Guid userId)
    {
        return WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            db.Users.Add(BuildUser(userId));
            await db.SaveChangesAsync();
            return true;
        });
    }

    private Task BindUserRolesAsync(Guid userId, params Guid[] roleIds)
    {
        return WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            foreach (var roleId in roleIds)
            {
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
            }
            await db.SaveChangesAsync();
            return true;
        });
    }

    private static ApplicationRole BuildRole(string code, Guid? tenantId, bool isActive)
    {
        // Name/NormalizedName must be globally unique (RoleNameIndex UNIQUE on NormalizedName);
        // Code is the value under test (matched by GetUserRoleCodesAsync). Name is raw display.
        var displayName = $"disp_{Guid.NewGuid():N}";
        return new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = displayName,
            NormalizedName = displayName.ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            Code = code, // intentionally raw — SQL applies UPPER+TRIM
            RoleType = RoleType.Business,
            TenantId = tenantId,
            IsActive = isActive,
        };
    }

    private static ApplicationUser BuildUser(Guid userId)
    {
        var email = $"role.test.{userId:N}@example.vn";
        return new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "Role-test user",
            IsActive = true,
        };
    }
}
