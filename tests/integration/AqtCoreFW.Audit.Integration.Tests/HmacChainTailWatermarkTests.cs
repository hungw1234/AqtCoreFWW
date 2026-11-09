using System.Security.Cryptography;
using System.Text;
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
/// Integration tests for the tail-attestation watermark write path
/// (<see cref="HmacChainService.RunSigningPassAsync"/> post-loop upsert, exercised through
/// <see cref="HmacChainService.SignTenantChainAsync"/> and <see cref="HmacChainService.BackfillTenantChainAsync"/>)
/// and its rollout-heal wiring in <see cref="HmacChainService.EnqueueSignForBackloggedChainsAsync"/>.
///
/// Scope: WRITE half only — no <c>VerifyAsync</c> tail check exists yet (that ships next). These tests
/// read the <c>[audit].[ChainWatermarks]</c> row back via raw SQL and independently recompute the
/// expected HMAC (same payload formula the production code uses:
/// <c>wm|{tenantId-or-"system"}|{lastSeq}|{head}</c>) rather than calling the service's private
/// helper — this proves the watermark is verifiable by anyone holding the key and the documented
/// formula, not merely self-consistent with whatever the implementation happens to compute.
///
/// Named-tenant tests use fresh GUIDs (no cross-test collision, no cleanup needed). The one test that
/// touches the shared NULL/system chain cleans up the rows it seeds so it does not perturb
/// <c>HmacChainPerTenantTests.NullTenantChain_VerifiesIndependently</c>'s exact-count assertion on
/// that same shared chain.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class HmacChainTailWatermarkTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Fixed 32-byte test key — no Vault dependency, matches the sibling HmacChain*Tests convention.
    private static readonly byte[] TestKey =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30,
        0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38,
        0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40
    ];

    public HmacChainTailWatermarkTests(WebAppFixture app) => _app = app;

    // ── TEST 1: incremental sign writes a watermark with a valid, independently-recomputable HMAC ──

    [Fact]
    public async Task IncrementalSign_WritesWatermark_WithValidHmac()
    {
        var tenantId = Guid.NewGuid();
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId);

        var watermark = await ReadWatermarkAsync(tenantId);
        watermark.Should().NotBeNull("signing a non-empty chain must write a watermark row");

        var (lastSeq, chainHead, watermarkHmac, _) = watermark!.Value;
        lastSeq.Should().Be(3, "three unsigned rows were signed in one pass");

        var lastRowSig = await ReadHmacSignatureAtSeqAsync(tenantId, lastSeq);
        chainHead.Should().Be(lastRowSig, "ChainHead must equal the signature of the highest-SequenceId row");

        watermarkHmac.Should().Be(
            ComputeExpectedWatermarkHmac(tenantId, lastSeq, chainHead),
            "WatermarkHmac must recompute to the same value under the same key + documented payload formula");
    }

    // ── TEST 2: backfill re-sign updates the watermark in the same pass ──

    [Fact]
    public async Task Backfill_UpdatesWatermark_SamePass()
    {
        var tenantId = Guid.NewGuid();
        // Seed rows with a deliberately wrong chain (SequenceId=99, garbage signature) — mirrors
        // HmacChainBackfillTests.SeedWronglySignedRowsAsync.
        await SeedWronglySignedRowsAsync(tenantId, count: 2);

        var svc = NewService();
        await svc.BackfillTenantChainAsync(tenantId);

        var watermark = await ReadWatermarkAsync(tenantId);
        watermark.Should().NotBeNull("backfill re-signing a non-empty chain must write a watermark row in the same pass");

        var (lastSeq, chainHead, watermarkHmac, _) = watermark!.Value;
        lastSeq.Should().Be(2, "backfill assigns fresh SequenceIds 1..N across all N rows");

        var lastRowSig = await ReadHmacSignatureAtSeqAsync(tenantId, lastSeq);
        chainHead.Should().Be(lastRowSig, "ChainHead must equal the post-backfill signature of the last row");

        watermarkHmac.Should().Be(ComputeExpectedWatermarkHmac(tenantId, lastSeq, chainHead));
    }

    // ── TEST 3: a signed chain whose watermark row was deleted is healed by the sweep ──

    [Fact]
    public async Task DormantSignedChain_MissingWatermark_HealedBySweep()
    {
        var tenantId = Guid.NewGuid();
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);

        var enqueuer = Substitute.For<IBackgroundAuditJobEnqueuer>();
        var svc = NewServiceWithEnqueuer(enqueuer);

        // Sign normally — this also writes the watermark.
        await svc.SignTenantChainAsync(tenantId);
        var seeded = await ReadWatermarkAsync(tenantId);
        seeded.Should().NotBeNull("the initial sign must write a watermark");

        // Simulate rollout / corruption: the chain is fully signed but its watermark is gone.
        await DeleteWatermarkRowAsync(tenantId);
        (await ReadWatermarkAsync(tenantId)).Should().BeNull("test setup: watermark row must be gone before the heal");

        // The sweep must detect this signed-but-watermark-less chain and enqueue it.
        await svc.EnqueueSignForBackloggedChainsAsync();
        enqueuer.Received().EnqueueHmacChain(tenantId);

        // Running the incremental sign the sweep would dispatch heals the watermark even though
        // there are zero unsigned rows left: LastSequenceId is unchanged, HMAC is valid.
        await svc.SignTenantChainAsync(tenantId);
        var healed = await ReadWatermarkAsync(tenantId);
        healed.Should().NotBeNull("a 0-row incremental pass must re-create the watermark from the existing anchor");

        healed!.Value.LastSequenceId.Should().Be(
            seeded!.Value.LastSequenceId, "healing a 0-row pass must not change the attested chain length");
        healed.Value.WatermarkHmac.Should().Be(
            ComputeExpectedWatermarkHmac(tenantId, healed.Value.LastSequenceId, healed.Value.ChainHead));
    }

    // ── TEST 4: a chain with zero signed rows gets no watermark row ──

    [Fact]
    public async Task EmptyChain_NoWatermarkRow()
    {
        var tenantId = Guid.NewGuid(); // never seeded — the pass reads and signs nothing.
        var svc = NewService();

        await svc.SignTenantChainAsync(tenantId);

        (await ReadWatermarkAsync(tenantId)).Should().BeNull(
            "a chain with zero signed rows (lastSeq == 0) must not get a watermark row");
    }

    // ── TEST 5: signing the NULL/system chain writes a watermark keyed on TenantId IS NULL ──

    [Fact]
    public async Task NullTenantChain_WritesSystemWatermark()
    {
        // The NULL/system chain is shared global state across the whole collection — clean up the
        // row this test seeds so HmacChainPerTenantTests.NullTenantChain_VerifiesIndependently's
        // exact EntriesChecked assertion on the same chain is not perturbed.
        var entry = await SeedEntryAsync(null);
        try
        {
            var svc = NewService();
            await svc.SignTenantChainAsync(null);

            var watermark = await ReadWatermarkAsync(null);
            watermark.Should().NotBeNull("signing the null-tenant chain must write a watermark row with TenantId IS NULL");

            var (lastSeq, chainHead, watermarkHmac, _) = watermark!.Value;
            lastSeq.Should().BeGreaterThan(0, "the null chain has at least the row this test just signed");

            watermarkHmac.Should().Be(
                ComputeExpectedWatermarkHmac(tenantId: null, lastSeq, chainHead),
                "the null-tenant watermark must use the \"system\" tenant-key convention in its payload");
        }
        finally
        {
            await CleanupNullRowsAsync([entry.MessageId]);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────────────────

    private HmacChainService NewService()
    {
        var scope = _app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);
        return new HmacChainService(repo, keyResolver, NullLogger<HmacChainService>.Instance, GetConnectionString());
    }

    /// <summary>Service with a stub key resolver + substitute enqueuer so enqueue calls can be asserted.</summary>
    private HmacChainService NewServiceWithEnqueuer(IBackgroundAuditJobEnqueuer enqueuer)
    {
        var scope = _app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);
        var connStr = GetConnectionString();
        // adminConnectionString defaults to connStr (dev sa Default is db_owner) — bypasses the RLS
        // FILTER predicate so the cross-tenant watermark-missing/backlog queries see all tenants.
        return new HmacChainService(
            repo, keyResolver, NullLogger<HmacChainService>.Instance,
            connStr, adminConnectionString: null, guidGenerator: gen, jobEnqueuer: enqueuer);
    }

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.GetConnectionString()!;
    }

    /// <summary>Seeds an unsigned entry (HmacSignature="", SequenceId=0) for the given tenant (or null-tenant).</summary>
    private async Task<AuditLogEntry> SeedEntryAsync(Guid? tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var entry = AuditLogEntry.Create(
            gen: gen,
            tenantId: tenantId,
            userId: Guid.NewGuid(),
            userName: "watermark-test",
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

    /// <summary>
    /// Seeds rows that already carry an HmacSignature but with a wrong chain (SequenceId=99, garbage
    /// signature) — mirrors HmacChainBackfillTests.SeedWronglySignedRowsAsync so the backfill test
    /// here proves the SAME "recompute-all overwrites already-signed rows" behavior, plus the watermark.
    /// </summary>
    private async Task SeedWronglySignedRowsAsync(Guid tenantId, int count)
    {
        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@msgId, @tenantId, GETUTCDATE(), SYSDATETIMEOFFSET(),
                 'WatermarkBackfillTest', 'Audit', 'AuditLog', 'watermark-backfill-test',
                 'deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef', 99, 0, 0, 2)
            """;

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();

        for (var i = 0; i < count; i++)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@msgId", Guid.NewGuid());
            cmd.Parameters.AddWithValue("@tenantId", (object)tenantId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<(long LastSequenceId, string ChainHead, string WatermarkHmac, DateTimeOffset UpdatedAtUtc)?>
        ReadWatermarkAsync(Guid? tenantId)
    {
        var sql = tenantId.HasValue
            ? "SELECT LastSequenceId, ChainHead, WatermarkHmac, UpdatedAtUtc FROM [audit].[ChainWatermarks] WHERE TenantId = @t"
            : "SELECT LastSequenceId, ChainHead, WatermarkHmac, UpdatedAtUtc FROM [audit].[ChainWatermarks] WHERE TenantId IS NULL";

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        if (tenantId.HasValue)
            cmd.Parameters.AddWithValue("@t", tenantId.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return (reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDateTimeOffset(3));
    }

    private async Task DeleteWatermarkRowAsync(Guid? tenantId)
    {
        // ChainWatermarks has no immutability trigger (unlike AuditLogEntries), so a plain DELETE
        // from the db_owner test connection needs no audit_mutate_mode session-context bypass.
        var sql = tenantId.HasValue
            ? "DELETE FROM [audit].[ChainWatermarks] WHERE TenantId = @t"
            : "DELETE FROM [audit].[ChainWatermarks] WHERE TenantId IS NULL";

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        if (tenantId.HasValue)
            cmd.Parameters.AddWithValue("@t", tenantId.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> ReadHmacSignatureAtSeqAsync(Guid tenantId, long sequenceId)
    {
        const string sql = """
            SELECT HmacSignature FROM [audit].[AuditLogEntries]
            WHERE TenantId = @t AND SequenceId = @seq
            """;
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@t", tenantId);
        cmd.Parameters.AddWithValue("@seq", sequenceId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string
            ?? throw new InvalidOperationException($"No AuditLogEntries row found at SequenceId={sequenceId} for tenant {tenantId}.");
    }

    /// <summary>
    /// Removes the given NULL-tenant AuditLogEntries rows via the archive bypass — mirrors
    /// HmacChainSweepTests' DisposeAsync cleanup so the shared system chain is left as this test
    /// found it.
    /// </summary>
    private async Task CleanupNullRowsAsync(IReadOnlyCollection<Guid> messageIds)
    {
        if (messageIds.Count == 0)
            return;

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var ctxCmd = conn.CreateCommand();
        ctxCmd.CommandText = "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'archive', @read_only=0;";
        await ctxCmd.ExecuteNonQueryAsync();

        foreach (var messageId in messageIds)
        {
            await using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM [audit].[AuditLogEntries] WHERE MessageId = @id;";
            delCmd.Parameters.Add(new SqlParameter("@id", messageId));
            await delCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Independently recomputes the expected WatermarkHmac using the SAME payload formula the
    /// production code documents (HmacChainService.BuildWatermarkPayload / ComputeWatermarkHmac):
    /// <c>wm|{tenantId-or-"system"}|{lastSeq}|{head}</c> under HMAC-SHA256 with the test key. This is
    /// deliberately a standalone re-implementation (not a call into the private production helper)
    /// so the test proves the watermark is verifiable by an independent party holding the key and the
    /// documented formula — not merely self-consistent with the implementation under test.
    /// </summary>
    private static string ComputeExpectedWatermarkHmac(Guid? tenantId, long lastSeq, string head)
    {
        var tenantKey = tenantId?.ToString() ?? "system";
        var payload = $"wm|{tenantKey}|{lastSeq}|{head}";
        using var hmac = new HMACSHA256(TestKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
