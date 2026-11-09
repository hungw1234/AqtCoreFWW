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
/// Integration tests for the verify-side tail-attestation watermark check
/// (<see cref="HmacChainService.VerifyViaRepositoryAsync"/> / <see cref="HmacChainService.VerifyViaRawConnAsync"/>,
/// both routed through the shared <c>EvaluateWatermark</c> decision helper).
///
/// The attack modeled throughout: an attacker who has DB write access but NOT the HMAC key deletes
/// the K newest rows of a tenant chain (or otherwise tampers the watermark row itself) without being
/// able to recompute a valid watermark, because doing so requires the key. All attacks use the
/// archive-mode bypass (same as the sibling HmacChainPerTenantTests tamper tests) — the attacker is
/// modeled as a privileged-enough DB principal (sa in tests), not as someone routing through the
/// application, so this exercises exactly the "DB write access without the key" threat model the
/// watermark closes.
///
/// Each test seeds rows, signs them (which — since Phase 1 — also writes/updates the watermark),
/// attacks, then verifies. The watermark itself carries its own HMAC (same key/algorithm as the
/// chain), so an attacker without the key cannot forge a new watermark to hide the attack.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class HmacChainTailTruncationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Fixed 32-byte test key — no Vault dependency, distinct value from sibling suites (irrelevant,
    // since each test uses its own fresh tenant, but keeps the byte pattern visually distinguishable).
    private static readonly byte[] TestKey =
    [
        0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
        0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F, 0x60,
        0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F, 0x70
    ];

    public HmacChainTailTruncationTests(WebAppFixture app) => _app = app;

    // ── TEST 1 (AC2, RED-first): tail truncation is detected by the watermark ──────────────────────
    // Red intent: against PRE-fix code (no watermark read in VerifyAsync) the remaining dense chain
    // (1..N-K) is internally consistent — every HMAC still valid, no hole — so verify reports GREEN.
    // This is the tail-truncation gap. Post-fix, the watermark (LastSeq=N, written when all N rows
    // existed) no longer matches maxSeenSeq=N-K, so verify must report IsValid=false with a
    // tail-truncation detail.

    [Fact]
    public async Task TailTruncation_DetectedByWatermark()
    {
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            await SeedEntryAsync(tenantId);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId); // writes watermark LastSequenceId=5

        // Attacker: DB write access, no HMAC key. Deletes the 2 newest rows via the archive bypass
        // without touching the watermark (an attacker without the key cannot recompute a valid one).
        await DeleteTailRowsAsync(tenantId, keepCount: 3);

        var result = await svc.VerifyAsync(tenantId);

        result.IsValid.Should().BeFalse(
            "the watermark attests LastSequenceId=5 but the chain's observed max SequenceId is now 3 " +
            "— an attacker deleted the tail without the key to update the watermark");
        result.TamperDetail.Should().Contain("Tail truncation");
        result.EntriesChecked.Should().Be(3, "only the 3 surviving rows are streamed");
    }

    // ── TEST 2 (AC3): lowering LastSequenceId without recomputing the HMAC is detected ──────────────

    [Fact]
    public async Task WatermarkSeqLowered_WithoutHmacRecompute_Detected()
    {
        var tenantId = Guid.NewGuid();
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId);

        // Attacker tries to hide a truncation by hand-editing LastSequenceId down — but cannot
        // recompute WatermarkHmac without the key, so it goes stale relative to the new payload.
        await LowerWatermarkSeqWithoutRecomputeAsync(tenantId);

        var result = await svc.VerifyAsync(tenantId);

        result.IsValid.Should().BeFalse("WatermarkHmac no longer matches (tenant, LastSequenceId, ChainHead) after the hand-edit");
        result.TamperDetail.Should().Contain("tampered");
    }

    // ── TEST 3 (AC4): deleting the watermark row on a non-empty chain is detected ────────────────────

    [Fact]
    public async Task WatermarkRowDeleted_OnNonEmptyChain_Detected()
    {
        var tenantId = Guid.NewGuid();
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId);

        // Attacker deletes the watermark row entirely to revert to pre-rollout "no tail check".
        await DeleteWatermarkRowAsync(tenantId);

        var result = await svc.VerifyAsync(tenantId);

        result.IsValid.Should().BeFalse("a signed (non-empty) chain with no watermark row must fail — otherwise deleting the watermark trivially bypasses the tail check");
        result.TamperDetail.Should().Contain("Missing chain watermark");
    }

    // ── TEST 4 (AC5): a backfill re-sign updates the watermark in the same pass — verify GREEN after ──

    [Fact]
    public async Task BackfillReSign_ReVerifiesGreen()
    {
        var tenantId = Guid.NewGuid();

        // Seed rows with a deliberately wrong chain (mirrors HmacChainBackfillTests / HmacChainTailWatermarkTests).
        await SeedWronglySignedRowsAsync(tenantId, count: 3);

        var svc = NewService();

        var before = await svc.VerifyAsync(tenantId);
        before.IsValid.Should().BeFalse("seeded rows carry a deliberately wrong chain before backfill");

        await svc.BackfillTenantChainAsync(tenantId);

        var after = await svc.VerifyAsync(tenantId);

        after.IsValid.Should().BeTrue("backfill re-signs every row AND rewrites the watermark in the same pass, so verify must be clean afterwards");
        after.EntriesChecked.Should().Be(3);
    }

    // ── TEST 5 (regression guard): a healthy signed chain with a watermark verifies green ───────────

    [Fact]
    public async Task HealthyChain_WithWatermark_Green()
    {
        var tenantId = Guid.NewGuid();
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);
        await SeedEntryAsync(tenantId);

        var svc = NewService();
        await svc.SignTenantChainAsync(tenantId);

        var result = await svc.VerifyAsync(tenantId);

        result.IsValid.Should().BeTrue("ordinary signed chain with a matching watermark must verify clean — the watermark check must not false-fail on the healthy case");
        result.EntriesChecked.Should().Be(4);
    }

    // ── TEST 6 (AC7): both verify paths share the same watermark decision logic ─────────────────────
    // Drives the identical tail-truncation attack through the EF-repository ctor
    // (VerifyViaRepositoryAsync) and the raw-connection ctor (VerifyViaRawConnAsync) and asserts both
    // report the same failure — proving EvaluateWatermark is a single shared source, not two
    // independently-maintained copies that could silently diverge.

    [Fact]
    public async Task BothVerifyPaths_ShareWatermarkLogic()
    {
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 4; i++)
            await SeedEntryAsync(tenantId);

        var repoSvc = NewService(); // 4-arg ctor: _repository != null -> VerifyViaRepositoryAsync
        await repoSvc.SignTenantChainAsync(tenantId);

        await DeleteTailRowsAsync(tenantId, keepCount: 2);

        var rawConnSvc = NewRawConnService(); // 3-arg ctor: _repository == null -> VerifyViaRawConnAsync

        var repoResult = await repoSvc.VerifyAsync(tenantId);
        var rawResult = await rawConnSvc.VerifyAsync(tenantId);

        repoResult.IsValid.Should().BeFalse("EF-repository verify path must detect the same tail truncation");
        rawResult.IsValid.Should().BeFalse("raw-connection verify path must detect the same tail truncation");

        repoResult.TamperDetail.Should().Contain("Tail truncation");
        rawResult.TamperDetail.Should().Contain("Tail truncation");
        rawResult.TamperDetail.Should().Be(repoResult.TamperDetail, "both paths route through the same EvaluateWatermark helper, so the detail text must be identical, not merely similar");
        rawResult.EntriesChecked.Should().Be(repoResult.EntriesChecked);
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>4-arg ctor (EF repository present) — VerifyAsync dispatches to VerifyViaRepositoryAsync.</summary>
    private HmacChainService NewService()
    {
        var scope = _app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);
        return new HmacChainService(repo, keyResolver, NullLogger<HmacChainService>.Instance, GetConnectionString());
    }

    /// <summary>3-arg constrained-login ctor (no EF repository) — VerifyAsync dispatches to VerifyViaRawConnAsync.</summary>
    private HmacChainService NewRawConnService()
    {
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);
        return new HmacChainService(GetConnectionString(), keyResolver, NullLogger<HmacChainService>.Instance);
    }

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.GetConnectionString()!;
    }

    /// <summary>Seeds an unsigned entry (HmacSignature="", SequenceId=0) for the given tenant.</summary>
    private async Task<AuditLogEntry> SeedEntryAsync(Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var entry = AuditLogEntry.Create(
            gen: gen,
            tenantId: tenantId,
            userId: Guid.NewGuid(),
            userName: "tail-truncation-test",
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
    /// here proves the watermark is rewritten alongside the chain re-sign.
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
                 'TailTruncationBackfillTest', 'Audit', 'AuditLog', 'tail-truncation-backfill-test',
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

    /// <summary>
    /// Models the tail-truncation attacker: DB write access, no HMAC key. Deletes the tail rows (highest
    /// SequenceId first) down to <paramref name="keepCount"/> surviving rows, via the archive-mode
    /// bypass (same pattern as HmacChainPerTenantTests.DeletedRow_Detected) — WITHOUT touching the
    /// watermark row, because an attacker without the key cannot recompute a valid WatermarkHmac.
    /// </summary>
    private async Task DeleteTailRowsAsync(Guid tenantId, int keepCount)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();

        var idsToDelete = new List<Guid>();
        const string selectSql = """
            SELECT MessageId
            FROM [audit].[AuditLogEntries]
            WHERE TenantId = @t AND SequenceId > @keep
            ORDER BY SequenceId
            """;
        await using (var selectCmd = new SqlCommand(selectSql, conn))
        {
            selectCmd.Parameters.AddWithValue("@t", tenantId);
            selectCmd.Parameters.AddWithValue("@keep", keepCount);
            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                idsToDelete.Add(reader.GetGuid(0));
        }

        await using var ctxCmd = conn.CreateCommand();
        ctxCmd.CommandText = "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'archive', @read_only=0;";
        await ctxCmd.ExecuteNonQueryAsync();

        foreach (var id in idsToDelete)
        {
            await using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM [audit].[AuditLogEntries] WHERE MessageId = @id;";
            delCmd.Parameters.Add(new SqlParameter("@id", id));
            await delCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Simulates an attacker hand-editing the watermark to hide a truncation, WITHOUT recomputing
    /// WatermarkHmac (which requires the key) — leaves the HMAC stale relative to the new payload.
    /// </summary>
    private async Task LowerWatermarkSeqWithoutRecomputeAsync(Guid tenantId)
    {
        const string sql = """
            UPDATE [audit].[ChainWatermarks]
               SET LastSequenceId = LastSequenceId - 1
             WHERE TenantId = @t
            """;
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@t", tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>ChainWatermarks has no immutability trigger, so a plain DELETE needs no mutate-mode bypass.</summary>
    private async Task DeleteWatermarkRowAsync(Guid tenantId)
    {
        const string sql = "DELETE FROM [audit].[ChainWatermarks] WHERE TenantId = @t";
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@t", tenantId);
        await cmd.ExecuteNonQueryAsync();
    }
}
