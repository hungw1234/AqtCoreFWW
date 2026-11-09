using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Organization.Integration.Tests.Clients;

/// <summary>
/// RT2-15 Phase 04 — real-DB integration tests for IOrgUnitModuleClient temporal methods.
///
/// OU-I1: ResolveActiveUsersInOrgUnitsAsync — active temporal assignment → user returned.
/// OU-I2: ResolveActiveUsersInOrgUnitsAsync — expired assignment (ValidTo in past) → excluded.
/// OU-I3: ResolveActiveUsersInOrgUnitsAsync — IsActive=0 assignment → excluded.
/// OU-I4: GetUserOrgUnitCodesAsync — active assignment → org unit code returned (canonical).
/// OU-I5: GetUserOrgUnitCodesAsync — cross-tenant org unit → excluded (TenantId filter).
///
/// Each test uses unique tenant + org unit + user IDs to avoid cross-test contention.
/// Uses WebAppFixture to ensure migrations applied; raw SQL helpers for seeding.
/// INVARIANT 260525-E: tenantId mandatory on all IOrgUnitModuleClient calls.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class OrgUnitModuleClientTemporalTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private readonly string _connStr;

    public OrgUnitModuleClientTemporalTests(WebAppFixture app, SqlServerFixture sql)
    {
        _ = app.Server; // force lazy host boot → migrations applied
        _app = app;
        _connStr = sql.ConnectionString;
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-I1: Active assignment → user included in resolved set
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveActiveUsersInOrgUnitsAsync_ActiveAssignment_ReturnsUser()
    {
        var tenantId = Guid.NewGuid();
        var userId = await InsertIdentityUserAsync(tenantId);
        var (orgUnitId, code) = await InsertOrgUnitAsync(tenantId, code: null);
        await InsertAssignmentAsync(userId, orgUnitId, tenantId,
            isActive: true, validFrom: DateTimeOffset.UtcNow.AddDays(-1), validTo: null);

        var result = await ExecAsync(c =>
            c.ResolveActiveUsersInOrgUnitsAsync([code], tenantId));

        result.Should().Contain(userId,
            "user with active temporal assignment to org unit must be in resolved set");
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-I2: Expired assignment (ValidTo in past) → excluded
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveActiveUsersInOrgUnitsAsync_ExpiredAssignment_Excluded()
    {
        var tenantId = Guid.NewGuid();
        var userId = await InsertIdentityUserAsync(tenantId);
        var (orgUnitId, code) = await InsertOrgUnitAsync(tenantId, code: null);
        await InsertAssignmentAsync(userId, orgUnitId, tenantId,
            isActive: true,
            validFrom: DateTimeOffset.UtcNow.AddDays(-10),
            validTo: DateTimeOffset.UtcNow.AddDays(-1));

        var result = await ExecAsync(c =>
            c.ResolveActiveUsersInOrgUnitsAsync([code], tenantId));

        result.Should().NotContain(userId,
            "ValidTo in the past means assignment expired — user must NOT be in resolved set");
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-I3: IsActive=0 assignment → excluded
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveActiveUsersInOrgUnitsAsync_InactiveAssignment_Excluded()
    {
        var tenantId = Guid.NewGuid();
        var userId = await InsertIdentityUserAsync(tenantId);
        var (orgUnitId, code) = await InsertOrgUnitAsync(tenantId, code: null);
        await InsertAssignmentAsync(userId, orgUnitId, tenantId,
            isActive: false, validFrom: DateTimeOffset.UtcNow.AddDays(-1), validTo: null);

        var result = await ExecAsync(c =>
            c.ResolveActiveUsersInOrgUnitsAsync([code], tenantId));

        result.Should().NotContain(userId,
            "IsActive=0 must exclude assignment regardless of temporal window");
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-I4: GetUserOrgUnitCodesAsync — active assignment → canonical code returned
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserOrgUnitCodesAsync_ActiveAssignment_ReturnsCanonicalCode()
    {
        var tenantId = Guid.NewGuid();
        var userId = await InsertIdentityUserAsync(tenantId);
        var rawCode = $"  ou_phong_test_{Guid.NewGuid():N}  ";
        var canonical = rawCode.Trim().ToUpperInvariant();
        var (orgUnitId, _) = await InsertOrgUnitAsync(tenantId, code: rawCode);
        await InsertAssignmentAsync(userId, orgUnitId, tenantId,
            isActive: true, validFrom: DateTimeOffset.UtcNow.AddDays(-1), validTo: null);

        var result = await ExecAsync(c =>
            c.GetUserOrgUnitCodesAsync(userId, tenantId));

        result.Should().Contain(canonical,
            "SQL UPPER(LTRIM(RTRIM(o.Code))) must return canonical form");
        result.Should().NotContain(rawCode,
            "non-canonical (raw with whitespace/lowercase) must NOT be returned");
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-I5: Cross-tenant org unit → excluded
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserOrgUnitCodesAsync_CrossTenantOrgUnit_Excluded()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = await InsertIdentityUserAsync(tenantA);

        // OrgUnit belongs to tenant B — should NOT appear in tenant A query
        var (orgUnitId, code) = await InsertOrgUnitAsync(tenantB, code: null);
        await InsertAssignmentAsync(userId, orgUnitId, tenantB,
            isActive: true, validFrom: DateTimeOffset.UtcNow.AddDays(-1), validTo: null);

        var result = await ExecAsync(c =>
            c.GetUserOrgUnitCodesAsync(userId, tenantA));

        result.Should().NotContain(code,
            "org unit belonging to tenant B must not appear in tenant A query (cross-tenant defense)");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private Task<TResult> ExecAsync<TResult>(Func<IOrgUnitModuleClient, Task<TResult>> action)
    {
        return WithScopeAsync(async sp =>
        {
            var client = sp.GetRequiredService<IOrgUnitModuleClient>();
            return await action(client);
        });
    }

    private async Task<TResult> WithScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<Guid> InsertIdentityUserAsync(Guid tenantId)
    {
        var id = Guid.NewGuid();
        var code = $"OU-TEST-{id:N}"[..30];
        const string sql = """
            INSERT INTO [identity].AspNetUsers
              (Id, TenantId, EmployeeCode, FullName, UserName, NormalizedUserName,
               Email, NormalizedEmail, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled,
               LockoutEnabled, AccessFailedCount, IsActive, ClearanceLevel,
               AuthSource, MustChangePassword, SecurityStamp, ConcurrencyStamp, CreatedAtUtc, ExtraProperties)
            VALUES
              (@Id, @TenantId, @Code, 'OrgUnit Test User', @Code, UPPER(@Code),
               @Email, UPPER(@Email), 1, 0, 0,
               1, 0, 1, 0,
               'LOCAL', 0, @Stamp, @Stamp, SYSUTCDATETIME(), '{}');
            """;
        await using var conn = new SqlConnection(_connStr);
        await conn.ExecuteAsync(sql, new
        {
            Id = id,
            TenantId = tenantId,
            Code = code,
            Email = $"{code}@orgunit-test.local",
            Stamp = Guid.NewGuid().ToString("N"),
        });
        return id;
    }

    /// <summary>
    /// Inserts an OrgUnit and returns (Id [long IDENTITY], canonical code).
    /// OrgUnit is T2: Id is DB-assigned bigint IDENTITY; use OUTPUT INSERTED.Id.
    /// </summary>
    private async Task<(long Id, string Code)> InsertOrgUnitAsync(Guid tenantId, string? code)
    {
        var rawCode = code ?? $"OU_TEST_{Guid.NewGuid():N}";
        var canonical = rawCode.Trim().ToUpperInvariant();
        const string sql = """
            INSERT INTO [organization].[OrgUnits]
              (PublicId, TenantId, Name, NameEn, Code, ParentId, Level, IsActive, IsDeleted,
               CreatedAt, CreatedBy, ClassificationLevel)
            OUTPUT INSERTED.Id
            VALUES
              (@PublicId, @TenantId, @Code, @Code, @Code, NULL, 1, 1, 0,
               SYSUTCDATETIME(), @TenantId, 0);
            """;
        await using var conn = new SqlConnection(_connStr);
        var id = await conn.ExecuteScalarAsync<long>(sql, new
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            Code = rawCode,
        });
        return (id, canonical);
    }

    private async Task InsertAssignmentAsync(
        Guid userId, long orgUnitId, Guid tenantId,
        bool isActive, DateTimeOffset validFrom, DateTimeOffset? validTo)
    {
        const string sql = """
            INSERT INTO [organization].UserOrgUnitAssignments
              (UserId, OrgUnitId, RoleInOrg, ValidFrom, ValidTo, IsActive,
               TenantId, CreatedAt, IsDeleted, CreatedBy, ClassificationLevel)
            VALUES
              (@UserId, @OrgUnitId, 'MEMBER', @ValidFrom, @ValidTo, @IsActive,
               @TenantId, SYSUTCDATETIME(), 0, '00000000-0000-0000-0000-000000000000', 0);
            """;
        await using var conn = new SqlConnection(_connStr);
        await conn.ExecuteAsync(sql, new
        {
            UserId = userId,
            OrgUnitId = orgUnitId,
            ValidFrom = validFrom,
            ValidTo = validTo,
            IsActive = isActive ? 1 : 0,
            TenantId = tenantId,
        });
    }
}
