using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Verifies that the asymmetric RLS predicates on <c>audit.AuditLogEntries</c> and
/// <c>audit.SecurityEventLogs</c> (added by <c>20260512142044_AddAuditNullableTenantRls</c>)
/// are enforced correctly for a non-db_owner SQL login.
///
/// All tests connect via <see cref="RlsPolicyPresenceFixture.ConstrainedConnectionString"/>
/// (<c>rls_test_app</c> — SELECT/INSERT/UPDATE/DELETE but NOT db_owner). The <c>sa</c>-based
/// <see cref="RlsPolicyPresenceFixture.ConnectionString"/> is used only for seeding.
///
/// PREDICATE SHAPE (AuditLogEntries / SecurityEventLogs):
///   FILTER  : IS_MEMBER('db_owner')=1
///             OR @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
///             OR (@TenantId IS NULL AND TRY_CAST(SESSION_CONTEXT(N'audit_admin_bypass') AS bit) = 1)
///   BLOCK AI: IS_MEMBER('db_owner')=1
///             OR @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
///             OR @TenantId IS NULL        ← allows audit pipeline writers (no session context set)
///
/// Pool-leak safety: <see cref="PoolLeak_BypassFlag_DoesNotSurvive_PoolRecycle"/> uses a
/// dedicated connection string with Max Pool Size=1 to ensure the same physical connection is
/// reused. It asserts SESSION_CONTEXT(N'audit_admin_bypass') IS NULL after pool return —
/// verifying sp_reset_connection clears the flag (no @read_only=1 is used anywhere, which is
/// intentional: the flag MUST be clearable by pool reset for safety).
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class AuditRlsEnforcementTests
{
    // Deterministic per-test Guids — avoid cross-test interference in shared DB.
    private static readonly Guid TenantA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly RlsPolicyPresenceFixture _fixture;

    public AuditRlsEnforcementTests(RlsPolicyPresenceFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Test 1: TenantA cannot see TenantB rows ──────────────────────────────

    [Fact]
    public async Task TenantA_CannotSee_TenantB_AuditRows()
    {
        // Seed via sa (db_owner) — no RLS predicate applies to sa.
        var msgA = Guid.NewGuid();
        var msgB = Guid.NewGuid();
        var msgNull = Guid.NewGuid();
        await SeedAuditLogEntryAsync(msgA, TenantA, "Login", "Seed TenantA");
        await SeedAuditLogEntryAsync(msgB, TenantB, "Login", "Seed TenantB");
        await SeedAuditLogEntryAsync(msgNull, null,    "Login", "Seed NullTenant");

        // Constrained connection — set session TenantId = A.
        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await SetSessionTenantAsync(conn, TenantA);

        var rows = await SelectAuditMessageIdsAsync(conn, new[] { msgA, msgB, msgNull });

        // Positive assertion first — proves the FILTER predicate isn't blocking everything.
        rows.Should().Contain(msgA, "TenantA session must see its own rows");

        // Negative assertions — the critical RLS checks.
        rows.Should().NotContain(msgB,    "TenantA session must NOT see TenantB rows");
        rows.Should().NotContain(msgNull, "TenantA session without bypass must NOT see NULL-tenant rows");
    }

    // ── Test 2: Admin with bypass sees NULL-tenant rows ──────────────────────

    [Fact]
    public async Task Admin_WithBypass_Sees_NullTenantAuditRows()
    {
        // Seed a NULL-tenant row via sa.
        var msgNull = Guid.NewGuid();
        await SeedAuditLogEntryAsync(msgNull, null, "SystemEvent", "Admin bypass test seed");

        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        // No TenantId in session — set only the admin bypass flag.
        await SetSessionAuditAdminBypassAsync(conn, enable: true);

        var rows = await SelectAuditMessageIdsAsync(conn, new[] { msgNull });

        rows.Should().Contain(msgNull,
            "admin with audit_admin_bypass='true' must see NULL-tenant system events");
    }

    // ── Test 3: Admin without bypass does NOT see NULL-tenant rows ────────────

    [Fact]
    public async Task Admin_WithoutBypass_DoesNotSee_NullTenantAuditRows()
    {
        // Seed a NULL-tenant row via sa.
        var msgNull = Guid.NewGuid();
        await SeedAuditLogEntryAsync(msgNull, null, "SystemEvent", "No-bypass test seed");

        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        // No session context at all — neither TenantId nor audit_admin_bypass.

        var rows = await SelectAuditMessageIdsAsync(conn, new[] { msgNull });

        rows.Should().NotContain(msgNull,
            "constrained user without session context must NOT see NULL-tenant rows");
    }

    // ── Test 4a: Audit writer can INSERT NULL-tenant row ─────────────────────

    [Fact]
    public async Task AuditWriter_CanInsert_NullTenantRow_WithConstrainedLogin()
    {
        // BLOCK AFTER INSERT predicate for null: OR @TenantId IS NULL — always allowed.
        // This validates the asymmetric design: server-side audit writers (no session context)
        // can emit system-wide events without a db_owner connection.
        var msgId = Guid.NewGuid();

        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        // No session context — simulates audit pipeline writer.

        // INSERT must succeed — exception here = test failure.
        await InsertAuditLogEntryAsync(conn, msgId, tenantId: null, "SystemEvent", "Writer test");

        // Verify the row is visible via sa.
        var inserted = await SelectAuditMessageIdsSaAsync(new[] { msgId });
        inserted.Should().Contain(msgId, "NULL-tenant INSERT must succeed for audit pipeline writers");
    }

    // ── Test 4b: INSERT with mismatched TenantId is blocked ──────────────────

    [Fact]
    public async Task AuditWriter_CannotInsert_ForeignTenantRow_WhenSessionIsTenantA()
    {
        // BLOCK AFTER INSERT: @TenantId(B) != SESSION_CONTEXT TenantId(A), not db_owner, not NULL.
        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await SetSessionTenantAsync(conn, TenantA);

        // INSERT a row with TenantId = TenantB (mismatch) → must be blocked.
        var msgId = Guid.NewGuid();
        var act = async () => await InsertAuditLogEntryAsync(conn, msgId, TenantB, "BlockTest", "Mismatch");

        await act.Should()
            .ThrowAsync<SqlException>()
            .Where(ex => ex.Number == 33504,
                "SQL Server error 33504 = BLOCK predicate violation");
    }

    // ── Test 5: Pool-leak — bypass flag does NOT survive pool recycle ─────────

    [Fact]
    public async Task PoolLeak_BypassFlag_DoesNotSurvive_PoolRecycle()
    {
        // Use a single-connection pool so the same physical connection is reused.
        // This simulates the request-pool scenario where one request sets audit_admin_bypass
        // and the next request picks up the same physical connection.
        var poolCs = BuildSingleConnectionPoolString(_fixture.ConstrainedConnectionString);

        // Round 1: open, set bypass, close (return to pool).
        await using (var conn1 = new SqlConnection(poolCs))
        {
            await conn1.OpenAsync();
            await SetSessionAuditAdminBypassAsync(conn1, enable: true);

            // Verify the flag is visible on this connection (sanity).
            var bypass = await GetSessionContextStringAsync(conn1, "audit_admin_bypass");
            bypass.Should().Be("true", "bypass must be visible before pool return");
        }
        // conn1 returns to pool here. sp_reset_connection fires on return.

        // Round 2: open a new SqlConnection — same pool → same physical connection.
        await using var conn2 = new SqlConnection(poolCs);
        await conn2.OpenAsync();
        await SetSessionTenantAsync(conn2, TenantB); // set TenantId but NOT bypass

        // THE CRITICAL ASSERTION: bypass flag must be NULL after pool reset.
        var bypassAfterReset = await GetSessionContextStringAsync(conn2, "audit_admin_bypass");
        bypassAfterReset.Should().BeNull(
            "sp_reset_connection must have cleared audit_admin_bypass; a non-null value here " +
            "would mean the flag leaked across logical connections and the policy is unsafe");

        // Corroboration: a NULL-tenant row seeded via sa must NOT be visible (no bypass).
        var msgNull = Guid.NewGuid();
        await SeedAuditLogEntryAsync(msgNull, null, "PoolLeakTest", "Pool leak probe");

        var rows = await SelectAuditMessageIdsAsync(conn2, new[] { msgNull });
        rows.Should().NotContain(msgNull,
            "with TenantB session but no bypass, NULL-tenant rows must remain invisible");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildSingleConnectionPoolString(string baseCs)
    {
        var csb = new SqlConnectionStringBuilder(baseCs)
        {
            MaxPoolSize   = 1,
            MinPoolSize   = 1,
            Pooling       = true,
        };
        return csb.ConnectionString;
    }

    private async Task SeedAuditLogEntryAsync(
        Guid messageId, Guid? tenantId, string action, string userAgent)
    {
        // Seed via sa (db_owner) — bypasses RLS BLOCK predicate and the
        // tr_AuditLog_NoMutate trigger (which only blocks DELETE, not INSERT).
        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await InsertAuditLogEntryAsync(conn, messageId, tenantId, action, userAgent);
    }

    private static async Task InsertAuditLogEntryAsync(
        SqlConnection conn, Guid messageId, Guid? tenantId, string action, string userAgent)
    {
        // Minimal INSERT satisfying NOT NULL constraints.
        // AuditLogEntry is a T3 plain-class (NOT an AuditableEntity subclass) — no IsDeleted column.
        // OccurredAt: datetimeoffset — use SYSDATETIMEOFFSET() via parameter.
        // Category + Severity: stored as int; 0 = AppAccess / Low defaults.
        // HmacSignature: NOT NULL varchar — empty string is valid (HMAC filled post-insert in prod).
        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@MessageId, @TenantId, GETUTCDATE(), SYSDATETIMEOFFSET(),
                 @Action, 'Tests', 'AuditLog', @UserName,
                 '', 0, 0, 0, 2)
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MessageId", messageId);
        cmd.Parameters.Add(new SqlParameter("@TenantId",
            tenantId.HasValue ? tenantId.Value : DBNull.Value));
        cmd.Parameters.AddWithValue("@Action", action);
        cmd.Parameters.AddWithValue("@UserName", userAgent);

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
        // Filter by MessageId list to avoid cross-test noise in shared DB.
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

    private static async Task SetSessionTenantAsync(SqlConnection conn, Guid tenantId)
    {
        await using var cmd = new SqlCommand(
            $"EXEC sp_set_session_context @key=N'TenantId', @value=N'{tenantId}', @read_only=0;",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SetSessionAuditAdminBypassAsync(SqlConnection conn, bool enable)
    {
        var value = enable ? "N'true'" : "NULL";
        await using var cmd = new SqlCommand(
            $"EXEC sp_set_session_context @key=N'audit_admin_bypass', @value={value}, @read_only=0;",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string?> GetSessionContextStringAsync(SqlConnection conn, string key)
    {
        await using var cmd = new SqlCommand(
            $"SELECT CAST(SESSION_CONTEXT(N'{key}') AS nvarchar(100))", conn);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result as string;
    }
}
