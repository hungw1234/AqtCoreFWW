using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Audit.Integration.Tests;

/// <summary>
/// Integration tests that define correctness for the per-tenant HMAC chain with signer-assigned SequenceId.
///
/// Key invariants proved here:
///   1. Each tenant has an independent chain — signing/verifying one tenant does not bleed into another.
///   2. Field tampering, deletion, and insertion of rows are detected.
///   3. InternalId insert-time != commit-order: rows arriving late (low InternalId, committed after
///      the first signing pass) get a higher SequenceId in the next signing pass — verify order by
///      SequenceId is always consistent, so commit-order variation never causes false-tamper.
///   4. Mid-chain holes (unsigned rows between two signed rows) are detected, not silently skipped.
///   5. NULL-tenant chain verifies independently of any named-tenant chain.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class HmacChainPerTenantTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Fixed 32-byte test HMAC key — consistent across sign and verify within each test.
    private static readonly byte[] TestKey = new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    };

    public HmacChainPerTenantTests(WebAppFixture app) => _app = app;

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates HmacChainService with a fixed test key resolver so tests are independent of Vault.
    /// The resolver is a stub — deterministic, no network calls, 32 bytes.
    /// Uses the 4-arg primary ctor: repo (for VerifyAsync) + explicit connection string (for raw batch).
    /// </summary>
    private HmacChainService NewService()
    {
        var scope = _app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);
        var connStr = GetConnectionString();
        return new HmacChainService(repo, keyResolver, NullLogger<HmacChainService>.Instance, connStr);
    }

    private AuditDbContext NewDb()
    {
        var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    }

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.GetConnectionString()!;
    }

    private IGuidGenerator NewGen()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
    }

    /// <summary>
    /// Seeds an entry for the given tenant (or null-tenant) and returns it.
    /// Entry starts unsigned (HmacSignature = "", SequenceId = 0).
    /// </summary>
    private async Task<AuditLogEntry> SeedEntryAsync(Guid? tenantId)
    {
        await using var db = NewDb();
        var gen = NewGen();
        var entry = AuditLogEntry.Create(
            gen: gen,
            tenantId: tenantId,
            userId: Guid.NewGuid(),
            userName: "chain-test",
            action: "Create",
            moduleName: "Audit",
            resourceType: "Document",
            resourceId: Guid.NewGuid().ToString(),
            dataSnapshot: null,
            ipAddress: "127.0.0.1",
            correlationId: null,
            category: AuditCategory.AppAccess,
            severity: AuditSeverity.Low);
        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    // ── TEST 1: two independent tenant chains — signing one does not pollute the other ──

    [Fact]
    public async Task TwoTenantsInterleaved_VerifyEachClean()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Interleave inserts to ensure no accidental ordering by InternalId across tenants.
        await SeedEntryAsync(tenantA);
        await SeedEntryAsync(tenantB);
        await SeedEntryAsync(tenantA);
        await SeedEntryAsync(tenantB);
        await SeedEntryAsync(tenantA);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantA);
        await svc.SignTenantChainAsync(tenantB);

        var resultA = await svc.VerifyAsync(tenantA);
        var resultB = await svc.VerifyAsync(tenantB);

        resultA.IsValid.Should().BeTrue("tenant A chain should verify clean after signing");
        resultA.EntriesChecked.Should().Be(3);

        resultB.IsValid.Should().BeTrue("tenant B chain should verify clean after signing");
        resultB.EntriesChecked.Should().Be(2);
    }

    // ── TEST 2: field tamper detected ──

    [Fact]
    public async Task TamperedField_Detected()
    {
        var tenantId = Guid.NewGuid();
        var entry = await SeedEntryAsync(tenantId);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId);

        // Directly mutate a field in DB using the bypass mode (same as trigger tests).
        var connStr = GetConnectionString();
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var ctxCmd = conn.CreateCommand();
        ctxCmd.CommandText = "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'archive', @read_only=0;";
        await ctxCmd.ExecuteNonQueryAsync();
        await using var tampCmd = conn.CreateCommand();
        tampCmd.CommandText = "UPDATE [audit].[AuditLogEntries] SET Action = N'TAMPERED' WHERE MessageId = @id;";
        tampCmd.Parameters.Add(new SqlParameter("@id", (object)entry.MessageId));
        await tampCmd.ExecuteNonQueryAsync();

        var result = await svc.VerifyAsync(tenantId);

        result.IsValid.Should().BeFalse("tampering the Action field must invalidate the HMAC");
        result.FirstTamperedEntryId.Should().Be(entry.MessageId);
    }

    // ── TEST 3: deleted row detected (shorter chain = lower EntriesChecked, sig mismatch) ──

    [Fact]
    public async Task DeletedRow_Detected()
    {
        var tenantId = Guid.NewGuid();
        var entry1 = await SeedEntryAsync(tenantId);
        var entry2 = await SeedEntryAsync(tenantId);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId);

        // Delete the first entry — second entry's previousHash now references a missing link.
        var connStr = GetConnectionString();
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var ctxCmd = conn.CreateCommand();
        ctxCmd.CommandText = "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'archive', @read_only=0;";
        await ctxCmd.ExecuteNonQueryAsync();
        await using var delCmd = conn.CreateCommand();
        delCmd.CommandText = "DELETE FROM [audit].[AuditLogEntries] WHERE MessageId = @id;";
        delCmd.Parameters.Add(new SqlParameter("@id", (object)entry1.MessageId));
        await delCmd.ExecuteNonQueryAsync();

        var result = await svc.VerifyAsync(tenantId);

        // Chain for tenantId now starts with entry2 whose previousHash was computed from entry1's sig.
        // With entry1 gone, verify starts with empty previousHash, which won't match entry2's stored sig.
        result.IsValid.Should().BeFalse("deleting a chained entry must break HMAC verification");
        result.FirstTamperedEntryId.Should().Be(entry2.MessageId);
    }

    // ── TEST 4: row inserted between signed rows detected ──

    [Fact]
    public async Task InsertedRow_Detected()
    {
        var tenantId = Guid.NewGuid();
        var entry1 = await SeedEntryAsync(tenantId);
        var entry2 = await SeedEntryAsync(tenantId);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId);

        // Capture SequenceIds after signing, then inject a row between the two.
        await using var db = NewDb();
        var signed1 = await db.AuditLogEntries.AsNoTracking()
            .FirstAsync(e => e.MessageId == entry1.MessageId);
        var signed2 = await db.AuditLogEntries.AsNoTracking()
            .FirstAsync(e => e.MessageId == entry2.MessageId);

        signed1.SequenceId.Should().BeLessThan(signed2.SequenceId, "signer assigns increasing SequenceIds");

        // Inject a row with SequenceId between the two signed rows — simulates insertion.
        var connStr = GetConnectionString();
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var ctxCmd = conn.CreateCommand();
        ctxCmd.CommandText = "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'signature', @read_only=0;";
        await ctxCmd.ExecuteNonQueryAsync();

        // Shift entry2 up by 100 to make room for the injected row.
        var newSeq2 = signed2.SequenceId + 100;
        await using var shiftCmd = conn.CreateCommand();
        shiftCmd.CommandText = $"UPDATE [audit].[AuditLogEntries] SET SequenceId = {newSeq2} WHERE MessageId = @id;";
        shiftCmd.Parameters.Add(new SqlParameter("@id", (object)entry2.MessageId));
        await shiftCmd.ExecuteNonQueryAsync();

        // Insert an injected row with a mid-range SequenceId and a forged signature.
        var gen = NewGen();
        var injectedId = gen.NewIdMonotonic();
        var midSeq = signed1.SequenceId + 1;
        await using var insCmd = conn.CreateCommand();
        insCmd.CommandText = $"""
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, CreatedAt, TenantId, UserId, UserName, Action, ModuleName, ResourceType, ResourceId,
                 DataSnapshot, IpAddress, UserAgent, OccurredAt, HmacSignature, CorrelationId, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@mid, GETUTCDATE(), @tid, NULL, 'injected', 'Inject', 'Audit', 'Document', 'injected',
                 NULL, '127.0.0.1', NULL, GETUTCDATE(), 'deadbeef', NULL, {midSeq}, 0, 0, 2)
            """;
        insCmd.Parameters.Add(new SqlParameter("@mid", (object)injectedId));
        insCmd.Parameters.Add(new SqlParameter("@tid", (object)tenantId));
        await insCmd.ExecuteNonQueryAsync();

        var result = await svc.VerifyAsync(tenantId);

        result.IsValid.Should().BeFalse("an injected row with a forged signature must break chain verification");
    }

    // ── TEST 5: out-of-order commit does NOT cause false tamper ──
    // InternalId insert-time != commit-order. A row with a lower InternalId that arrived late
    // (committed after a first signing pass) enters the next signing pass and gets a higher SequenceId.
    // Verify by SequenceId (sign-order) must remain clean.

    [Fact]
    public async Task OutOfOrderCommit_NoFalseTamper()
    {
        var tenantId = Guid.NewGuid();

        // Round 1: seed two entries and sign them.
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId);

        // Round 2: seed one more entry (simulating a late-commit row that arrives after the first batch).
        await SeedEntryAsync(tenantId);
        await svc.SignTenantChainAsync(tenantId);

        // All three must verify clean — the late-arrival got the highest SequenceId.
        var result = await svc.VerifyAsync(tenantId);

        result.IsValid.Should().BeTrue("late-committed rows signed in a later pass get higher SequenceIds; verify by sign-order is always consistent");
        result.EntriesChecked.Should().Be(3);
    }

    // ── TEST 6: mid-chain unsigned row is detected as a chain hole ──
    // An unsigned row sitting between two signed rows means the chain is broken — the signed rows
    // after the hole were signed without linking through the unsigned row, violating continuity.

    [Fact]
    public async Task MidChainUnsignedRow_DetectedAsHole()
    {
        var tenantId = Guid.NewGuid();

        // Sign two entries first so they get SequenceId 1 and 2.
        var entry1 = await SeedEntryAsync(tenantId);
        var entry2 = await SeedEntryAsync(tenantId);
        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId);

        // Now insert an unsigned row that sits BETWEEN entry1 and entry2 by SequenceId
        // by shifting entry2 up and inserting a hole row between them.
        await using var db = NewDb();
        var signed1 = await db.AuditLogEntries.AsNoTracking().FirstAsync(e => e.MessageId == entry1.MessageId);
        var signed2 = await db.AuditLogEntries.AsNoTracking().FirstAsync(e => e.MessageId == entry2.MessageId);

        var connStr = GetConnectionString();
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var ctxCmd = conn.CreateCommand();
        ctxCmd.CommandText = "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'signature', @read_only=0;";
        await ctxCmd.ExecuteNonQueryAsync();

        // Shift entry2 up to make room for the unsigned hole between seq1 and the shifted seq2.
        await using var shiftCmd = conn.CreateCommand();
        shiftCmd.CommandText = $"UPDATE [audit].[AuditLogEntries] SET SequenceId = {signed2.SequenceId + 10} WHERE MessageId = @id;";
        shiftCmd.Parameters.Add(new SqlParameter("@id", (object)entry2.MessageId));
        await shiftCmd.ExecuteNonQueryAsync();

        // Insert an unsigned (HmacSignature='') row with SequenceId between signed1 and shifted signed2.
        var gen = NewGen();
        var holeId = gen.NewIdMonotonic();
        var holeSeq = signed1.SequenceId + 1;
        await using var insCmd = conn.CreateCommand();
        insCmd.CommandText = $"""
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, CreatedAt, TenantId, UserId, UserName, Action, ModuleName, ResourceType, ResourceId,
                 DataSnapshot, IpAddress, UserAgent, OccurredAt, HmacSignature, CorrelationId, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@hid, GETUTCDATE(), @tid, NULL, 'unsigned-hole', 'Create', 'Audit', 'Document', 'hole',
                 NULL, '127.0.0.1', NULL, GETUTCDATE(), '', NULL, {holeSeq}, 0, 0, 2)
            """;
        insCmd.Parameters.Add(new SqlParameter("@hid", (object)holeId));
        insCmd.Parameters.Add(new SqlParameter("@tid", (object)tenantId));
        await insCmd.ExecuteNonQueryAsync();

        // GetByTenantOrderedAsync filters to SequenceId > 0, so the hole row (holeSeq > 0 here)
        // will appear in the verify stream. signed2 was shifted to holeSeq+10, creating a gap of 9
        // between the hole row and signed2 — the gap detector will flag it.
        // Alternatively the hole row has a bad signature that won't match the chain.
        var result = await svc.VerifyAsync(tenantId);

        result.IsValid.Should().BeFalse("an unsigned row inserted between signed rows breaks chain integrity");
    }

    // ── TEST 7: null-tenant chain verifies independently of named-tenant chains ──

    [Fact]
    public async Task NullTenantChain_VerifiesIndependently()
    {
        var namedTenant = Guid.NewGuid();

        // Seed entries for both null-tenant (system-level) and a named tenant.
        await SeedEntryAsync(null);
        await SeedEntryAsync(null);
        await SeedEntryAsync(namedTenant);

        var svc = NewService();
        await svc.SignTenantChainAsync(null);
        await svc.SignTenantChainAsync(namedTenant);

        var nullResult = await svc.VerifyAsync(null);
        var namedResult = await svc.VerifyAsync(namedTenant);

        nullResult.IsValid.Should().BeTrue("null-tenant chain verifies independently");
        nullResult.EntriesChecked.Should().Be(2);

        namedResult.IsValid.Should().BeTrue("named tenant chain is not affected by null-tenant signing");
        namedResult.EntriesChecked.Should().Be(1);
    }
}
