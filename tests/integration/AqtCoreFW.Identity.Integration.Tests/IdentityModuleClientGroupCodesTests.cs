using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP — real-DB integration tests for
/// <see cref="IIdentityModuleClient.GetUserGroupCodesAsync"/>.
///
/// Mirror of <see cref="IdentityModuleClientRoleNamesTests"/>; exercises the real Dapper SQL
/// against a Testcontainers SQL Server DB to lock the direct-membership semantic:
///   1. Direct UserGroupMembership row → group code returned.
///   2. UserGroup.IsActive=false → code NOT returned (soft-disable honored).
///   3. ApplicationUser.IsActive=false → code NOT returned (user-soft-disable honored).
///   4. Tenant scope: global (TenantId IS NULL) ∪ matching TenantId → returned; cross-tenant excluded.
///   5. Canonical UPPER+TRIM transformation against group code with whitespace+lowercase.
///
/// Each test uses unique tenant + group + user IDs to avoid Testcontainers cross-test contention.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class IdentityModuleClientGroupCodesTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public IdentityModuleClientGroupCodesTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task GetUserGroupCodesAsync_UserWithDirectMembership_ReturnsGroupCode()
    {
        var tenantId = Guid.NewGuid();
        var (userId, canonical) = await SeedUserWithDirectMembershipAsync(tenantId);

        var codes = await ExecAsync(c => c.GetUserGroupCodesAsync(userId, tenantId));

        codes.Should().Contain(canonical,
            "direct UserGroupMembership entry must surface in the group-code set");
    }

    [Fact]
    public async Task GetUserGroupCodesAsync_InactiveGroup_Excluded()
    {
        var tenantId = Guid.NewGuid();
        var (userId, canonical) = await SeedUserWithDirectMembershipAsync(tenantId, groupActive: false);

        var codes = await ExecAsync(c => c.GetUserGroupCodesAsync(userId, tenantId));

        codes.Should().NotContain(canonical,
            "UserGroup.IsActive=false MUST exclude the group code (soft-disable honored)");
    }

    [Fact]
    public async Task GetUserGroupCodesAsync_InactiveUser_Excluded()
    {
        var tenantId = Guid.NewGuid();
        var (userId, canonical) = await SeedUserWithDirectMembershipAsync(tenantId, userActive: false);

        var codes = await ExecAsync(c => c.GetUserGroupCodesAsync(userId, tenantId));

        codes.Should().NotContain(canonical,
            "ApplicationUser.IsActive=false MUST exclude the group code (user soft-disable honored)");
    }

    /// <summary>
    /// Tenant scope: global group (TenantId=NULL) returned for ANY tenant; tenant-A group
    /// returned only when query passes TenantId=A; tenant-B group excluded from a TenantId=A query.
    /// </summary>
    [Fact]
    public async Task GetUserGroupCodesAsync_TenantScope_IncludesGlobalAndMatching_ExcludesOther()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SeedUserAsync(userId);
        var (globalGroupId, globalCanonical) = await InsertGroupAsync(tenantId: null);
        var (tenantAGroupId, tenantACanonical) = await InsertGroupAsync(tenantId: tenantA);
        var (tenantBGroupId, tenantBCanonical) = await InsertGroupAsync(tenantId: tenantB);

        await BindUserGroupsAsync(userId, globalGroupId, tenantAGroupId, tenantBGroupId);

        var codes = await ExecAsync(c => c.GetUserGroupCodesAsync(userId, tenantA));

        codes.Should().Contain(globalCanonical,
            "global group (TenantId IS NULL) must be visible regardless of caller's tenant");
        codes.Should().Contain(tenantACanonical,
            "group for caller's tenant must be visible");
        codes.Should().NotContain(tenantBCanonical,
            "group for different tenant must NOT be visible (cross-tenant defense)");
    }

    /// <summary>
    /// SQL <c>UPPER(LTRIM(RTRIM(g.Code)))</c> canonicalization mirrors
    /// <c>WorkflowGraphValidator</c> Save-time canonicalization. Without LTRIM/RTRIM, codes with
    /// leading/trailing whitespace would not match resolver-pushed candidate-index refs.
    /// </summary>
    [Fact]
    public async Task GetUserGroupCodesAsync_GroupCodeWithWhitespaceAndCase_ReturnsCanonicalUpperTrim()
    {
        var tenantId = Guid.NewGuid();
        var core = $"group_review_{Guid.NewGuid():N}";
        var rawCode = $"  {core}  ";
        var canonical = core.Trim().ToUpperInvariant();
        var (userId, _) = await SeedUserWithDirectMembershipAsync(tenantId, rawCode: rawCode);

        var codes = await ExecAsync(c => c.GetUserGroupCodesAsync(userId, tenantId));

        codes.Should().Contain(canonical,
            "SQL UPPER(LTRIM(RTRIM(g.Code))) must produce canonical form matching " +
            "WorkflowGraphValidator Save-time canonicalization");
        codes.Should().NotContain(rawCode,
            "non-canonical form must not be surfaced");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
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

    private Task<(Guid UserId, string CanonicalGroupCode)> SeedUserWithDirectMembershipAsync(
        Guid tenantId,
        bool groupActive = true,
        bool userActive = true,
        string? rawCode = null)
    {
        return WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var userId = Guid.NewGuid();

            db.Users.Add(BuildUser(userId, isActive: userActive));
            await db.SaveChangesAsync();

            var (groupId, canonical) = await InsertGroupRawAsync(
                db, tenantId: tenantId, isActive: groupActive, rawCode: rawCode);

            db.Set<UserGroupMembership>().Add(new UserGroupMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GroupId = groupId,
                AddedAtUtc = DateTime.UtcNow,
                AddedBy = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();

            return (userId, canonical);
        });
    }

    private Task<(long Id, string Canonical)> InsertGroupAsync(Guid? tenantId)
    {
        return WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            return await InsertGroupRawAsync(db, tenantId, isActive: true, rawCode: null);
        });
    }

    private static async Task<(long Id, string Canonical)> InsertGroupRawAsync(
        IdentityDbContext db, Guid? tenantId, bool isActive, string? rawCode)
    {
        var publicId = Guid.NewGuid();
        var code = rawCode ?? $"TEST_GROUP_{Guid.NewGuid():N}";
        var canonical = code.Trim().ToUpperInvariant();
        var actor = Guid.NewGuid();

        const string sql = """
            INSERT INTO [identity].[UserGroups]
                (PublicId, Code, Name, IsActive, TenantId,
                 CreatedAt, CreatedBy, IsDeleted, ClassificationLevel)
            OUTPUT INSERTED.Id
            VALUES
                (@PublicId, @Code, @Code, @IsActive, @TenantId,
                 SYSUTCDATETIME(), @Actor, 0, 2);
            """;

        var connStr = db.Database.GetConnectionString()!;
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@PublicId", publicId);
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@TenantId", (object?)tenantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Actor", actor);
        var result = await cmd.ExecuteScalarAsync();
        return (Convert.ToInt64(result), canonical);
    }

    private Task SeedUserAsync(Guid userId)
    {
        return WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            db.Users.Add(BuildUser(userId, isActive: true));
            await db.SaveChangesAsync();
            return true;
        });
    }

    private Task BindUserGroupsAsync(Guid userId, params long[] groupIds)
    {
        return WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            foreach (var groupId in groupIds)
            {
                db.Set<UserGroupMembership>().Add(new UserGroupMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    GroupId = groupId,
                    AddedAtUtc = DateTime.UtcNow,
                    AddedBy = Guid.NewGuid(),
                });
            }
            await db.SaveChangesAsync();
            return true;
        });
    }

    private static ApplicationUser BuildUser(Guid userId, bool isActive)
    {
        var email = $"group.test.{userId:N}@example.vn";
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
