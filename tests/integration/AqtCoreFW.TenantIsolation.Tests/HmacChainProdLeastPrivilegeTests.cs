using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.Audit.Infrastructure.Services;
using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Verifies that <see cref="HmacChainService.SignTenantChainAsync"/> functions correctly when
/// running under a non-db_owner SQL login (<c>rls_test_app</c>).
///
/// WHY these tests live here, not in AqtCoreFW.Audit.Integration.Tests:
///   Audit.Integration.Tests uses WebAppFixture which connects as db_owner (sa). db_owner
///   bypasses RLS predicates entirely (IS_MEMBER('db_owner')=1 short-circuits the predicate).
///   A test using the db_owner connection would pass even if the RLS bypass-before-read is
///   missing from SignTenantChainAsync — the missing bypass would only surface in prod where
///   the app runs as a least-privilege login. These tests use ConstrainedConnectionString
///   (rls_test_app: SELECT/INSERT/UPDATE/DELETE, NOT db_owner) to catch exactly that gap.
///
/// Three scenarios:
///   1. Named-tenant signing under non-db_owner reads rows correctly and writes signatures.
///   2. NULL-tenant (system events) requires audit_admin_bypass to be set before the read.
///   3. After the batch completes, no session-context keys leak through the pooled connection.
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class HmacChainProdLeastPrivilegeTests
{
    // Fixed 32-byte test key — deterministic, no Vault dependency.
    private static readonly byte[] TestKey =
    [
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    ];

    private readonly RlsPolicyPresenceFixture _fixture;

    public HmacChainProdLeastPrivilegeTests(RlsPolicyPresenceFixture fixture)
    {
        _fixture = fixture;
    }

    // ── TEST 1: named-tenant signing under prod least-privilege login ─────────
    // This test MUST be RED if bypass-before-read is missing, because:
    //   - rls_test_app is not db_owner, so the FILTER predicate fires.
    //   - Named-tenant rows require SESSION_CONTEXT N'TenantId' to match the row's TenantId.
    //   - Without setting TenantId context, the signer's anchor read and unsigned-rows read
    //     both return 0 rows (filtered by RLS), so it has nothing to sign.
    //   - Result: signer signs zero rows; verify finds unsigned rows = chain never complete.

    [Fact]
    public async Task ProdLeastPrivilegeLogin_SignaturesWritten()
    {
        var tenantId = Guid.NewGuid();

        // Seed two rows via sa (db_owner bypasses RLS BLOCK).
        var msg1 = await SeedEntryAsync(tenantId);
        var msg2 = await SeedEntryAsync(tenantId);

        // Sign via a service that uses the CONSTRAINED (non-db_owner) connection string.
        var svc = NewServiceWithConstrainedConn();
        await svc.SignTenantChainAsync(tenantId);

        // Verify both rows are now signed (SequenceId > 0, HmacSignature != "").
        var (seq1, sig1) = await ReadSignatureAsync(msg1);
        var (seq2, sig2) = await ReadSignatureAsync(msg2);

        seq1.Should().BeGreaterThan(0, "signer must assign SequenceId > 0 under least-priv login");
        sig1.Should().NotBeNullOrEmpty("signer must write HmacSignature under least-priv login");
        seq2.Should().BeGreaterThan(0, "signer must assign SequenceId > 0 for second row");
        sig2.Should().NotBeNullOrEmpty("signer must write HmacSignature for second row");

        // Chains must be contiguous and different.
        seq2.Should().Be(seq1 + 1, "signer assigns contiguous SequenceIds within a tenant");
        sig1.Should().NotBe(sig2, "each entry must have a unique HMAC signature");
    }

    // ── TEST 2: null-tenant admin bypass — system events signed correctly ─────
    // NULL-tenant rows are hidden from non-db_owner callers unless audit_admin_bypass is set.
    // If the signer doesn't set this key before reading, it sees 0 rows and signs nothing.

    [Fact]
    public async Task NullTenant_AdminBypass_SignsSystemEvents()
    {
        // Seed two NULL-tenant system events via sa.
        var sysMsgA = await SeedEntryAsync(null);
        var sysMsgB = await SeedEntryAsync(null);

        var svc = NewServiceWithConstrainedConn();
        await svc.SignTenantChainAsync(null); // null-tenant = system chain

        var (seqA, sigA) = await ReadSignatureAsync(sysMsgA);
        var (seqB, sigB) = await ReadSignatureAsync(sysMsgB);

        seqA.Should().BeGreaterThan(0,
            "null-tenant signer must set audit_admin_bypass before reading so system rows are visible");
        sigA.Should().NotBeNullOrEmpty("system event must receive HMAC signature");
        seqB.Should().BeGreaterThan(0, "second system event must also be signed");
        sigB.Should().NotBeNullOrEmpty();

        seqB.Should().BeGreaterThan(seqA, "signer assigns increasing SequenceIds");
    }

    // ── TEST 3: no context leak after batch ───────────────────────────────────
    // After SignTenantChainAsync returns, the connection goes back to the pool. If any
    // session-context key (TenantId, audit_admin_bypass, audit_mutate_mode) was left set
    // with @read_only=0, sp_reset_connection clears it — but we validate this explicitly
    // by acquiring the same physical connection from a single-slot pool and probing.

    [Fact]
    public async Task NoContextLeak_AfterBatch()
    {
        // Single-slot pool ensures we get the same physical connection the signer returned.
        var singleSlotCs = BuildSingleSlotConstrainedConnectionString();
        var tenantId = Guid.NewGuid();
        await SeedEntryAsync(tenantId);

        // Sign: this opens and returns the connection (or closes it) internally.
        var svc = NewServiceWithConstrainedConn(singleSlotCs);
        await svc.SignTenantChainAsync(tenantId);

        // Now open a fresh logical connection on the same pool slot.
        await using var probe = new SqlConnection(singleSlotCs);
        await probe.OpenAsync();

        // None of the three context keys may survive pool reset.
        var tenantCtx   = await GetContextAsync(probe, "TenantId");
        var adminBypass = await GetContextAsync(probe, "audit_admin_bypass");
        var mutateMode  = await GetContextAsync(probe, "audit_mutate_mode");

        tenantCtx.Should().BeNull(
            "TenantId session-context must be cleared before connection returns to pool");
        adminBypass.Should().BeNull(
            "audit_admin_bypass must be cleared to prevent null-tenant rows leaking to subsequent callers");
        mutateMode.Should().BeNull(
            "audit_mutate_mode must be cleared to prevent write-bypass leaking to subsequent callers");
    }

    // ── TEST 4: cross-tenant backfill enumeration requires elevated (db_owner) connection ─────────
    // Under the non-db_owner runtime login (rls_test_app), DISTINCT TenantId returns only NULL
    // (audit_admin_bypass exposes only NULL-tenant rows). Named-tenant rows are invisible without
    // knowing each TenantId in advance — which requires the tenant list (circular).
    // Only a db_owner-capable connection (sa here, AuditAdmin in prod) breaks the cycle.
    // This test MUST fail against old code where ReadDistinctTenantIdsAsync used _connectionString
    // (the constrained rls_test_app connection) instead of _adminConnectionString.

    [Fact]
    public async Task NamedTenant_BackfillUnderElevatedEnum_EnumeratesAndBackfills()
    {
        var tenantId = Guid.NewGuid();

        // Seed rows with a broken chain (simulate old wrong-chain rows) via sa (db_owner bypasses BLOCK).
        await SeedWronglySignedEntryAsync(tenantId);
        await SeedWronglySignedEntryAsync(tenantId);

        // Verify the chain is invalid before backfill.
        var serviceForVerify = NewServiceWithConstrainedConn();
        var before = await serviceForVerify.VerifyAsync(tenantId);
        before.IsValid.Should().BeFalse("seeded rows have broken chain — must be invalid before backfill");

        // Run BackfillAllTenantsAsync with:
        //   _connectionString    = rls_test_app (non-db_owner) — used for per-tenant signing passes
        //   _adminConnectionString = sa (db_owner) — used for cross-tenant enumeration
        // OLD code: ReadDistinctTenantIdsAsync used rls_test_app → DISTINCT TenantId returns only NULL
        //   → named tenant silently skipped → chain never backfilled → VerifyAsync still invalid.
        // NEW code: uses sa for enumeration → all tenant IDs returned → named tenant backfilled.
        var serviceWithElevatedEnum = NewServiceWithElevatedEnumConn();
        await serviceWithElevatedEnum.BackfillAllTenantsAsync();

        // Chain must now be valid — the named tenant was enumerated and backfilled.
        var after = await serviceForVerify.VerifyAsync(tenantId);
        after.IsValid.Should().BeTrue(
            "BackfillAllTenantsAsync must use the elevated (db_owner) connection to enumerate named-tenant " +
            "IDs; per-tenant signing passes continue under the runtime (non-db_owner) connection");
        after.EntriesChecked.Should().BeGreaterThanOrEqualTo(2,
            "both seeded rows must be in the re-signed chain");
    }

    // ── TEST 5 (AC6): tail-attestation watermark round-trips under non-db_owner least-privilege ─────
    // Guards feedback-sa-dbowner-false-greens-rls-assertions: the watermark check logic itself is
    // RLS-independent (ChainWatermarks carries no RLS policy), so a db_owner test would validate the
    // LOGIC correctly either way — but the GRANT (SELECT/INSERT/UPDATE on ChainWatermarks) is
    // prod-load-bearing, and only a non-db_owner login can surface a missing grant (prod SqlException
    // 229). Signs under rls_test_app (proves the GRANT is sufficient for the upsert), deletes the tail
    // rows via sa + archive-mode (attacker has no key, does not touch the watermark), then verifies
    // under rls_test_app again (proves the GRANT is sufficient for the read-back + HMAC recompute).

    [Fact]
    public async Task LeastPriv_SignWritesWatermark_AndVerifyDetectsTailTruncation()
    {
        var tenantId = Guid.NewGuid();
        var msg1 = await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);
        var msg3 = await SeedEntryAsync(tenantId);

        var svc = NewServiceWithConstrainedConn();

        // Sign under rls_test_app — if the GRANT on ChainWatermarks were missing, the post-loop
        // upsert would throw SqlException 229 (permission denied) rather than silently no-op.
        await svc.SignTenantChainAsync(tenantId);

        var (seq1, _) = await ReadSignatureAsync(msg1);
        seq1.Should().BeGreaterThan(0, "signing under least-privilege must still succeed with the watermark upsert in the same pass");

        // Attacker: sa (db_owner) write access, no HMAC key. Deletes the newest row via the archive
        // bypass without touching the watermark — mirrors the tail-truncation attack modeled in
        // HmacChainTailTruncationTests, run here under the least-privilege login end-to-end.
        await DeleteRowAsync(msg3);

        var result = await svc.VerifyAsync(tenantId);

        result.IsValid.Should().BeFalse(
            "verify under rls_test_app must detect the tail truncation via the watermark — this fails " +
            "closed (SqlException) rather than silently passing if the ChainWatermarks GRANT is missing");
        result.TamperDetail.Should().Contain("Tail truncation");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates HmacChainService wired to the CONSTRAINED (non-db_owner) connection string.
    /// The service bypasses EF and uses a raw SqlConnection — we pass the constrained string
    /// so it signs under the least-privilege login, exactly as prod does.
    /// </summary>
    private HmacChainService NewServiceWithConstrainedConn(string? connectionString = null)
    {
        var connStr = connectionString ?? _fixture.ConstrainedConnectionString;
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);

        // HmacChainService(connStr, keyResolver, logger) — the raw-conn ctor for constrained login.
        // If this overload does not exist yet, the test is RED (compilation error → red intent confirmed).
        return new HmacChainService(connStr, keyResolver, NullLogger<HmacChainService>.Instance);
    }

    /// <summary>
    /// Creates HmacChainService with the CONSTRAINED connection for per-tenant signing passes
    /// and the ELEVATED (sa / db_owner) connection for cross-tenant enumeration.
    /// This matches the prod split: runtime login = least-privilege; admin enumeration = db_owner.
    /// </summary>
    private HmacChainService NewServiceWithElevatedEnumConn()
    {
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);

        // No EF repository on this path — VerifyAsync falls back to raw-conn via the constrained ctor.
        // BackfillAllTenantsAsync only needs _connectionString (per-tenant passes) + _adminConnectionString.
        // The primary ctor is used here; repository is not needed for the backfill-only path.
        return new HmacChainService(
            repository: null,  // no EF repository needed for backfill-only path
            keyResolver: keyResolver,
            logger: NullLogger<HmacChainService>.Instance,
            connectionString: _fixture.ConstrainedConnectionString,
            adminConnectionString: _fixture.ConnectionString); // sa = db_owner = elevated enumeration
    }

    private string BuildSingleSlotConstrainedConnectionString()
    {
        var csb = new SqlConnectionStringBuilder(_fixture.ConstrainedConnectionString)
        {
            MaxPoolSize = 1,
            MinPoolSize = 1,
            Pooling     = true,
        };
        return csb.ConnectionString;
    }

    private async Task<Guid> SeedEntryAsync(Guid? tenantId)
    {
        var msgId = Guid.NewGuid();
        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@msgId, @tenantId, GETUTCDATE(), SYSDATETIMEOFFSET(),
                 'SystemEvent', 'Tests', 'AuditLog', 'prod-least-priv-test',
                 '', 0, 0, 0, 2)
            """;

        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa for seeding
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@msgId", msgId);
        cmd.Parameters.Add(new SqlParameter("@tenantId",
            tenantId.HasValue ? tenantId.Value : DBNull.Value));
        await cmd.ExecuteNonQueryAsync();
        return msgId;
    }

    /// <summary>
    /// Seeds a named-tenant row with a wrong chain (SequenceId=99, garbage signature) so
    /// VerifyAsync reports invalid before backfill and valid after — matching the broken-chain
    /// scenario that the full-system backfill is designed to fix.
    /// </summary>
    private async Task SeedWronglySignedEntryAsync(Guid tenantId)
    {
        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@msgId, @tenantId, GETUTCDATE(), SYSDATETIMEOFFSET(),
                 'BackfillLeastPrivTest', 'Tests', 'AuditLog', 'rls-backfill-test',
                 'deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef', 99, 0, 0, 2)
            """;

        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa for seeding
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@msgId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@tenantId", (object)tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Deletes a single AuditLogEntries row via sa + the archive-mode bypass (same pattern as
    /// HmacChainPerTenantTests.DeletedRow_Detected) — models the tail-truncation attacker (DB write
    /// access, no HMAC key) removing a signed row without touching the watermark.
    /// </summary>
    private async Task DeleteRowAsync(Guid messageId)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa for the attack
        await conn.OpenAsync();
        await using var ctxCmd = conn.CreateCommand();
        ctxCmd.CommandText = "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'archive', @read_only=0;";
        await ctxCmd.ExecuteNonQueryAsync();
        await using var delCmd = conn.CreateCommand();
        delCmd.CommandText = "DELETE FROM [audit].[AuditLogEntries] WHERE MessageId = @id;";
        delCmd.Parameters.Add(new SqlParameter("@id", messageId));
        await delCmd.ExecuteNonQueryAsync();
    }

    private async Task<(long SequenceId, string? Signature)> ReadSignatureAsync(Guid messageId)
    {
        const string sql = "SELECT SequenceId, HmacSignature FROM [audit].[AuditLogEntries] WHERE MessageId = @id";
        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa for reading back
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", messageId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Row {messageId} not found.");
        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task<string?> GetContextAsync(SqlConnection conn, string key)
    {
        await using var cmd = new SqlCommand(
            $"SELECT CAST(SESSION_CONTEXT(N'{key}') AS nvarchar(500))", conn);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result as string;
    }
}
