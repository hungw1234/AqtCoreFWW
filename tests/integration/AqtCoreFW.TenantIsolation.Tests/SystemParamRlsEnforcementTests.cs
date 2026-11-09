using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Verifies that the symmetric RLS predicate on <c>config.SystemParameters</c>
/// (added by <c>20260512152000_AddSystemParamRls</c>) is enforced correctly for a non-db_owner
/// SQL login.
///
/// All tests connect via <see cref="RlsPolicyPresenceFixture.ConstrainedConnectionString"/>
/// (<c>rls_test_app</c> — SELECT/INSERT/UPDATE/DELETE but NOT db_owner). The <c>sa</c>-based
/// connection is used only for seeding.
///
/// PREDICATE SHAPE (single TVF — reused for FILTER, BLOCK-AFTER-INSERT, BLOCK-AFTER-UPDATE):
///   IS_MEMBER('db_owner')=1
///   OR @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS nvarchar(100))
///   OR @TenantId IS NULL         ← global rows visible to ALL tenants (seeder path)
///
/// Design rationale: NULL TenantId = global/system parameter visible to all tenants. The
/// seeder writes these at startup without setting any session context — the NULL passthrough
/// is what makes the seeder pattern work under RLS (no db_owner connection required at runtime).
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class SystemParamRlsEnforcementTests
{
    private const string TenantA = "tenant-a-test";
    private const string TenantB = "tenant-b-test";

    private readonly RlsPolicyPresenceFixture _fixture;

    public SystemParamRlsEnforcementTests(RlsPolicyPresenceFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Test 6: Global row visible to all tenants ─────────────────────────────

    [Fact]
    public async Task SystemParameters_GlobalRow_VisibleToAllTenants()
    {
        // Seed via sa: one global param (TenantId=NULL) + one TenantA-specific override.
        var globalKey = $"rls.test.global.{Guid.NewGuid():N}";
        var tenantKey = $"rls.test.tenant.{Guid.NewGuid():N}";
        await SeedSystemParamAsync(globalKey, "global-value", tenantId: null);
        await SeedSystemParamAsync(tenantKey, "tenant-a-value", tenantId: TenantA);

        // Constrained connection — TenantA session.
        await using var connA = new SqlConnection(_fixture.ConstrainedConnectionString);
        await connA.OpenAsync();
        await SetSessionTenantAsync(connA, TenantA);

        var keysA = await SelectSystemParamKeysAsync(connA, new[] { globalKey, tenantKey });
        keysA.Should().Contain(globalKey,  "TenantA must see global (NULL-tenant) parameters");
        keysA.Should().Contain(tenantKey,  "TenantA must see its own tenant-specific parameter");

        // Constrained connection — TenantB session.
        await using var connB = new SqlConnection(_fixture.ConstrainedConnectionString);
        await connB.OpenAsync();
        await SetSessionTenantAsync(connB, TenantB);

        var keysB = await SelectSystemParamKeysAsync(connB, new[] { globalKey, tenantKey });
        keysB.Should().Contain(globalKey,    "TenantB must see global (NULL-tenant) parameters");
        keysB.Should().NotContain(tenantKey, "TenantB must NOT see TenantA-specific parameters");
    }

    // ── Test 7: Cannot write foreign-tenant row; can write NULL-tenant row ────

    [Fact]
    public async Task SystemParameters_CannotInsert_ForeignTenantRow_WhenSessionIsTenantA()
    {
        // BLOCK AFTER INSERT: @TenantId(B) != SESSION_CONTEXT TenantId(A), not db_owner, not NULL.
        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await SetSessionTenantAsync(conn, TenantA);

        var foreignKey = $"rls.test.foreign.{Guid.NewGuid():N}";
        var act = async () => await InsertSystemParamAsync(conn, foreignKey, "bad-value", TenantB);

        await act.Should()
            .ThrowAsync<SqlException>()
            .Where(ex => ex.Number == 33504,
                "SQL Server error 33504 = BLOCK predicate violation — foreign-tenant INSERT blocked");
    }

    [Fact]
    public async Task SystemParameters_CanInsert_NullTenantRow_UnderConstrainedLogin()
    {
        // BLOCK AFTER INSERT: OR @TenantId IS NULL — always passes (seeder pattern).
        // This verifies that a constrained login (e.g. runtime app user) can write global params
        // even without session TenantId set — mirrors SystemParamSeeder startup behaviour.
        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        // No session context — simulates SystemParamSeeder (runs at startup, no tenant set).

        var globalKey = $"rls.test.seeder.{Guid.NewGuid():N}";

        // INSERT must succeed — exception here = RLS predicate is incorrectly blocking seeders.
        await InsertSystemParamAsync(conn, globalKey, "seeded-value", tenantId: null);

        // Verify via sa.
        var keys = await SelectSystemParamKeysSaAsync(new[] { globalKey });
        keys.Should().Contain(globalKey, "seeder NULL-tenant INSERT must succeed under constrained login");
    }

    // ── Test 8: AuditLogPurgeJob smoke — NULL-tenant row purge under bypass ───
    //
    // The production AuditLogPurgeJob runs as db_owner (EF Core app user) and sets
    // audit_admin_bypass='true' to see NULL-tenant rows during bucket discovery.
    // This test proves the SQL-level equivalent: a constrained non-db_owner connection
    // that sets the bypass flag can DELETE NULL-tenant rows with audit_mutate_mode='archive'.
    // (DELETE on audit.AuditLogEntries requires the archive bypass due to tr_AuditLog_NoMutate.)
    // See: AuditLogPurgeJob.cs — ExecuteAsync() session-context setup block.

    [Fact]
    public async Task PurgeJobSmoke_ConstrainedConn_WithBypass_CanDelete_NullTenantExpiredRows()
    {
        // Seed an expired NULL-tenant row via sa.
        var msgId = Guid.NewGuid();
        await SeedExpiredAuditLogEntryAsync(msgId, tenantId: null);

        // Constrained connection: set both bypass flags (mirrors AuditLogPurgeJob setup).
        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await using (var bypassCmd = new SqlCommand(
            "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'archive', @read_only=0;",
            conn))
            await bypassCmd.ExecuteNonQueryAsync();
        await using (var adminBypassCmd = new SqlCommand(
            "EXEC sp_set_session_context @key=N'audit_admin_bypass', @value=N'true', @read_only=0;",
            conn))
            await adminBypassCmd.ExecuteNonQueryAsync();

        // Verify the row is visible under bypass (false-pass guard).
        var visible = await SelectAuditMessageIdsAsync(conn, new[] { msgId });
        visible.Should().Contain(msgId, "row must be visible with audit_admin_bypass before DELETE");

        // DELETE — should succeed because:
        // 1. audit_mutate_mode='archive' bypasses tr_AuditLog_NoMutate trigger.
        // 2. BLOCK predicate allows: @TenantId IS NULL (asymmetric BLOCK branch).
        await using (var deleteCmd = new SqlCommand(
            $"DELETE FROM [audit].[AuditLogEntries] WHERE MessageId = '{msgId}'", conn))
            await deleteCmd.ExecuteNonQueryAsync();

        // Verify deletion via sa.
        var afterDelete = await SelectAuditMessageIdsSaAsync(new[] { msgId });
        afterDelete.Should().NotContain(msgId, "expired NULL-tenant row must be deleted by purge job");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task SeedSystemParamAsync(string key, string value, string? tenantId)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await InsertSystemParamAsync(conn, key, value, tenantId);
    }

    private static async Task InsertSystemParamAsync(
        SqlConnection conn, string key, string value, string? tenantId)
    {
        // SystemParameters: Id (identity), Key (nvarchar(200) NOT NULL), Value (nvarchar(4000) NOT NULL),
        // DataType (int), Description (nvarchar(500) NOT NULL), IsEditable (bit),
        // TenantId (nvarchar(100) nullable), CreatedAt, IsDeleted, CreatedBy, ClassificationLevel.
        // KEY is a T-SQL reserved word — bracket it.
        const string sql = """
            INSERT INTO [config].[SystemParameters]
                ([Key], [Value], DataType, Description, IsEditable, TenantId,
                 CreatedAt, IsDeleted, CreatedBy, ClassificationLevel)
            VALUES
                (@Key, @Value, 0, 'RLS enforcement test row', 0, @TenantId,
                 GETUTCDATE(), 0, '00000000-0000-0000-0000-000000000000', 0)
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Key", key);
        cmd.Parameters.AddWithValue("@Value", value);
        cmd.Parameters.Add(new SqlParameter("@TenantId",
            tenantId is not null ? tenantId : DBNull.Value));

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<HashSet<string>> SelectSystemParamKeysSaAsync(IEnumerable<string> keys)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        return await SelectSystemParamKeysAsync(conn, keys);
    }

    private static async Task<HashSet<string>> SelectSystemParamKeysAsync(
        SqlConnection conn, IEnumerable<string> keys)
    {
        var keyList = string.Join(",", keys.Select(k => $"N'{k}'"));
        var sql = $"""
            SELECT [Key] FROM [config].[SystemParameters]
            WHERE [Key] IN ({keyList})
            """;

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    private async Task SeedExpiredAuditLogEntryAsync(Guid messageId, Guid? tenantId)
    {
        // Seed an audit log row with OccurredAt in the past (expired).
        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@MessageId, @TenantId, GETUTCDATE(),
                 DATEADD(DAY, -400, SYSDATETIMEOFFSET()),
                 'PurgeTest', 'Tests', 'AuditLog', 'purge-smoke-test',
                 '', 0, 0, 0, 2)
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MessageId", messageId);
        cmd.Parameters.Add(new SqlParameter("@TenantId",
            tenantId.HasValue ? tenantId.Value : DBNull.Value));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<HashSet<Guid>> SelectAuditMessageIdsSaAsync(IEnumerable<Guid> messageIds)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        return await SelectAuditMessageIdsAsync(conn, messageIds);
    }

    private static async Task<HashSet<Guid>> SelectAuditMessageIdsAsync(
        SqlConnection conn, IEnumerable<Guid> messageIds)
    {
        var idList = string.Join(",", messageIds.Select(g => $"'{g}'"));
        var sql = $"""
            SELECT MessageId FROM [audit].[AuditLogEntries]
            WHERE MessageId IN ({idList})
            """;

        var result = new HashSet<Guid>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetGuid(0));
        return result;
    }

    private static async Task SetSessionTenantAsync(SqlConnection conn, string tenantId)
    {
        await using var cmd = new SqlCommand(
            $"EXEC sp_set_session_context @key=N'TenantId', @value=N'{tenantId}', @read_only=0;",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
