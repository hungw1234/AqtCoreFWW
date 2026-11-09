using AqtCoreFW.Organization.Infrastructure.Seeding;
using AqtCoreFW.Tests.SharedFixtures;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace AqtCoreFW.Organization.Integration.Tests;

/// <summary>
/// Integration tests for SeedOrphanPurger — the self-healing orphan-cleanup logic
/// extracted from the assignment seeder. Validates:
///   1. Tenant-scoped purge removes orphans whose UserId+TenantId pair has no
///      matching identity.AspNetUsers row.
///   2. Valid (non-orphan) rows are preserved.
///   3. StaffPositionHistories purge mirrors UserOrgUnitAssignments predicate.
///   4. Sanity ceiling guard throws when orphan count exceeds limit.
///   5. Cross-tenant rows are NOT touched (P0 multi-tenant isolation).
///
/// Uses WebAppFixture solely for its migrated SQL Server schema — no HTTP calls,
/// no full BTC seeder run (which would seed 10k users).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SeedOrphanPurgerTests : IClassFixture<WebAppFixture>
{
    private readonly string _connStr;

    private static readonly Guid TenantBtc = Guid.Parse("a1b2c3d4-0000-0000-0000-00000000b7c1");
    private static readonly Guid TenantOther = Guid.Parse("99999999-0000-0000-0000-000000000099");

    // WebAppFixture (class-level) ensures Program.cs has booted → migrations applied.
    // WebApplicationFactory boots LAZILY — touching .Server (or CreateClient) forces Host build,
    // which runs MigrateAsync() on every DbContext. Without this, organization tables don't exist.
    // SqlServerFixture (collection-level) gives the raw connection string for setup SQL.
    public SeedOrphanPurgerTests(WebAppFixture app, SqlServerFixture sql)
    {
        _ = app.Server; // force lazy host boot → migrations applied
        _connStr = sql.ConnectionString;
    }

    [Fact]
    public async Task PurgeAsync_RemovesOrphansAndPreservesValidRows_TenantScoped()
    {
        // Arrange — clean slate for both tenants
        await ClearAssignmentsAsync(TenantBtc);
        await ClearAssignmentsAsync(TenantOther);

        var validUserId = await InsertIdentityUserAsync(TenantBtc, "T-PURGE-V1");
        var orphanUserId = Guid.NewGuid(); // never inserted into AspNetUsers
        const long unitId = 1L; // OrgUnitId is a loose bigint (no FK) — any value works

        await InsertAssignmentAsync(validUserId,  unitId, "Valid",  TenantBtc);
        await InsertAssignmentAsync(orphanUserId, unitId, "Orphan", TenantBtc);
        await InsertHistoryAsync(orphanUserId, unitId, "Orphan-History", TenantBtc);

        var orphanBefore = await CountOrphansAsync(TenantBtc);
        orphanBefore.Should().Be(1, "1 assignment row points to a non-existent user");

        // Act
        var purged = await SeedOrphanPurger.PurgeAsync(_connStr, TenantBtc, ceiling: 100);

        // Assert
        purged.Should().BeGreaterThanOrEqualTo(2, "1 assignment + 1 history orphan removed");

        var orphanAfter = await CountOrphansAsync(TenantBtc);
        orphanAfter.Should().Be(0, "all orphan assignments must be gone");

        var validRemaining = await CountAssignmentsForUserAsync(validUserId, TenantBtc);
        validRemaining.Should().Be(1, "valid (matched-user) row must NOT be deleted");
    }

    [Fact]
    public async Task PurgeAsync_ZeroOrphans_ReturnsZeroNoOp()
    {
        await ClearAssignmentsAsync(TenantBtc);
        var userId = await InsertIdentityUserAsync(TenantBtc, "T-PURGE-NOOP");
        await InsertAssignmentAsync(userId, 1L, "Clean", TenantBtc);

        var purged = await SeedOrphanPurger.PurgeAsync(_connStr, TenantBtc, ceiling: 100);

        purged.Should().Be(0, "fast path: no orphans → no delete issued");
    }

    [Fact]
    public async Task PurgeAsync_OrphanCountExceedsCeiling_ThrowsInvalidOperation()
    {
        await ClearAssignmentsAsync(TenantBtc);
        // Insert 3 orphans, set ceiling = 2 → must throw, must NOT delete
        for (int i = 0; i < 3; i++)
        {
            await InsertAssignmentAsync(Guid.NewGuid(), 1L, $"Orphan-{i}", TenantBtc);
        }

        var act = () => SeedOrphanPurger.PurgeAsync(_connStr, TenantBtc, ceiling: 2);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ceiling exceeded*");

        var stillThere = await CountOrphansAsync(TenantBtc);
        stillThere.Should().Be(3, "ceiling guard must abort BEFORE any DELETE runs");
    }

    [Fact]
    public async Task PurgeAsync_DoesNotTouchOtherTenantOrphans()
    {
        await ClearAssignmentsAsync(TenantBtc);
        await ClearAssignmentsAsync(TenantOther);

        // Orphan in OTHER tenant — purge for BTC tenant must leave it alone
        await InsertAssignmentAsync(Guid.NewGuid(), 1L, "OtherOrphan", TenantOther);

        var purged = await SeedOrphanPurger.PurgeAsync(_connStr, TenantBtc, ceiling: 100);

        purged.Should().Be(0, "BTC purge must not see other-tenant orphans (tenant-scoped)");
        var otherStill = await CountOrphansAsync(TenantOther);
        otherStill.Should().Be(1, "other tenant's orphan must survive BTC purge");
    }

    // ── DB helpers (raw SQL — bypass tenant filters + EF for setup speed) ──────

    private async Task<Guid> InsertIdentityUserAsync(Guid tenantId, string codePrefix)
    {
        var id = Guid.NewGuid();
        var code = $"{codePrefix}-{id:N}"[..30];
        // MustChangePassword is NOT-NULL with no DB default — must be supplied explicitly
        // or the INSERT fails with a NULL-violation before any orphan-purge assertion runs.
        const string sql = """
            INSERT INTO [identity].AspNetUsers
              (Id, TenantId, EmployeeCode, FullName, UserName, NormalizedUserName,
               Email, NormalizedEmail, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled,
               LockoutEnabled, AccessFailedCount, IsActive, ClearanceLevel,
               AuthSource, MustChangePassword, SecurityStamp, ConcurrencyStamp, CreatedAtUtc, ExtraProperties)
            VALUES
              (@Id, @TenantId, @Code, 'Test User', @Code, UPPER(@Code),
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
            Email = $"{code}@test.local",
            Stamp = Guid.NewGuid().ToString("N"),
        });
        return id;
    }

    // OrgUnitId is bigint (no FK), Id is bigint IDENTITY — omit Id so SQL assigns it.
    private async Task InsertAssignmentAsync(Guid userId, long orgUnitId, string role, Guid tenantId)
    {
        const string sql = """
            INSERT INTO [organization].UserOrgUnitAssignments
              (UserId, OrgUnitId, RoleInOrg, ValidFrom, IsActive,
               TenantId, CreatedAt, IsDeleted, CreatedBy, ClassificationLevel)
            VALUES
              (@UserId, @OrgUnitId, @Role, SYSDATETIMEOFFSET(), 1,
               @TenantId, SYSDATETIMEOFFSET(), 0, '00000000-0000-0000-0000-000000000000', 0);
            """;
        await using var conn = new SqlConnection(_connStr);
        await conn.ExecuteAsync(sql, new
        {
            UserId = userId,
            OrgUnitId = orgUnitId,
            Role = role,
            TenantId = tenantId,
        });
    }

    // OrgUnitId is bigint (no FK), Id is bigint IDENTITY — omit Id so SQL assigns it.
    private async Task InsertHistoryAsync(Guid userId, long orgUnitId, string title, Guid tenantId)
    {
        const string sql = """
            INSERT INTO [organization].StaffPositionHistories
              (UserId, OrgUnitId, PositionTitle, StartDate,
               TenantId, CreatedAt, IsDeleted, CreatedBy, ClassificationLevel)
            VALUES
              (@UserId, @OrgUnitId, @Title, SYSDATETIMEOFFSET(),
               @TenantId, SYSDATETIMEOFFSET(), 0, '00000000-0000-0000-0000-000000000000', 0);
            """;
        await using var conn = new SqlConnection(_connStr);
        await conn.ExecuteAsync(sql, new
        {
            UserId = userId,
            OrgUnitId = orgUnitId,
            Title = title,
            TenantId = tenantId,
        });
    }

    private async Task<int> CountOrphansAsync(Guid tenantId)
    {
        const string sql = """
            SELECT COUNT(*) FROM [organization].UserOrgUnitAssignments a
            WHERE a.TenantId = @TenantId
              AND NOT EXISTS (
                SELECT 1 FROM [identity].AspNetUsers u
                WHERE u.Id = a.UserId AND u.TenantId = a.TenantId
              );
            """;
        await using var conn = new SqlConnection(_connStr);
        return await conn.ExecuteScalarAsync<int>(sql, new { TenantId = tenantId });
    }

    private async Task<int> CountAssignmentsForUserAsync(Guid userId, Guid tenantId)
    {
        const string sql = """
            SELECT COUNT(*) FROM [organization].UserOrgUnitAssignments
            WHERE UserId = @UserId AND TenantId = @TenantId;
            """;
        await using var conn = new SqlConnection(_connStr);
        return await conn.ExecuteScalarAsync<int>(sql, new { UserId = userId, TenantId = tenantId });
    }

    private async Task ClearAssignmentsAsync(Guid tenantId)
    {
        const string sql = """
            DELETE FROM [organization].UserOrgUnitAssignments WHERE TenantId = @TenantId;
            DELETE FROM [organization].StaffPositionHistories WHERE TenantId = @TenantId;
            """;
        await using var conn = new SqlConnection(_connStr);
        await conn.ExecuteAsync(sql, new { TenantId = tenantId });
    }
}
