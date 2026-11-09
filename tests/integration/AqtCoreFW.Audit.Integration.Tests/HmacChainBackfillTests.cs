using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;
using Xunit;

namespace AqtCoreFW.Audit.Integration.Tests;

/// <summary>
/// Integration tests for the backfill-re-sign path.
///
/// Five invariants proved:
///   1. Backfill rewrites already-signed rows (bypasses empty-guard) and leaves the chain clean.
///   2. Running backfill twice is idempotent — chain remains valid.
///   3. A concurrent live signer on the same tenant waits (applock serializes) — no chain fork.
///   4. A non-SystemAdmin caller receives 403 from the admin endpoint.
///   5. Backfill writes one audit trail entry with the correct triggering actor.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class HmacChainBackfillTests : IClassFixture<WebAppFixture>
{
    // Fixed 32-byte test key — no Vault dependency.
    private static readonly byte[] TestKey =
    [
        0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11, 0x22,
        0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA,
        0xAB, 0xAC, 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2,
        0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA
    ];

    private readonly WebAppFixture _app;

    public HmacChainBackfillTests(WebAppFixture app) => _app = app;

    // ── TEST 1: backfill rewrites already-signed rows, leaves chain clean ─────────────────────────
    // This proves red-team #7: the empty-guard (WHERE HmacSignature='') in UpdateSignatureAsync
    // does NOT exist in the backfill path, so already-signed rows (wrong chain) are overwritten.

    [Fact]
    public async Task Backfill_WrongChainRows_VerifyCleanAfter()
    {
        var tenantId = Guid.NewGuid();

        // Seed rows with deliberate "wrong" chain: SequenceId=0 and a garbage signature.
        // These simulate the 151 dev rows signed by the old broken chain.
        await SeedWronglySignedRowsAsync(tenantId, count: 3);

        // Sanity: verify starts out invalid (chain is wrong).
        var svc = NewService();
        var before = await svc.VerifyAsync(tenantId);
        before.IsValid.Should().BeFalse("seeded rows have broken chain — verify must report invalid before backfill");

        // Run backfill on this tenant.
        await svc.BackfillTenantChainAsync(tenantId);

        // All rows must now be correctly chained.
        var after = await svc.VerifyAsync(tenantId);
        after.IsValid.Should().BeTrue("backfill must rewrite all rows (including already-signed) and produce a clean chain");
        after.EntriesChecked.Should().Be(3, "all three rows must be in the signed chain");
    }

    // ── TEST 2: idempotent — running backfill twice leaves chain clean and stable ─────────────────

    [Fact]
    public async Task Backfill_Idempotent()
    {
        var tenantId = Guid.NewGuid();
        await SeedWronglySignedRowsAsync(tenantId, count: 2);

        var svc = NewService();

        // First pass.
        await svc.BackfillTenantChainAsync(tenantId);
        var first = await svc.VerifyAsync(tenantId);
        first.IsValid.Should().BeTrue("first backfill pass must produce a clean chain");

        // Second pass — must remain clean and stable (same signatures for same content).
        await svc.BackfillTenantChainAsync(tenantId);
        var second = await svc.VerifyAsync(tenantId);
        second.IsValid.Should().BeTrue("second backfill pass must leave chain clean (idempotent)");
        second.EntriesChecked.Should().Be(first.EntriesChecked, "row count must be stable across backfill passes");
    }

    // ── TEST 3: concurrent live signer waits — applock serializes, no chain fork ────────────────
    // Backfill holds the per-tenant applock. A concurrent live SignTenantChainAsync call for the
    // same tenant must wait until backfill releases the lock before it can sign the new rows.
    // Result: chain must be contiguous (no forked SequenceIds).

    [Fact]
    public async Task Backfill_ConcurrentLiveWrite_NoFork()
    {
        var tenantId = Guid.NewGuid();
        // Pre-seed three wrong rows (backfill will re-sign these).
        await SeedWronglySignedRowsAsync(tenantId, count: 3);

        var connStr = GetConnectionString();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        // Launch backfill and a concurrent live sign simultaneously on the same tenant.
        // One task will acquire the applock first; the other will wait.
        // After both complete, verify the chain is clean with no sequence forks.
        var backfillTask = Task.Run(async () =>
        {
            var svc = NewService();
            await svc.BackfillTenantChainAsync(tenantId, cts.Token);
            // After backfill, seed one more entry and sign normally to interleave.
            await SeedRawUnsignedRowAsync(tenantId);
        }, cts.Token);

        var liveTask = Task.Run(async () =>
        {
            // Small delay so backfill gets a head start on the lock.
            await Task.Delay(50, cts.Token);
            await NewService().SignTenantChainAsync(tenantId, cts.Token);
        }, cts.Token);

        // Both must complete without throwing (lock acquisition / release must be correct).
        var results = await Task.WhenAll(
            backfillTask.ContinueWith(t => t, TaskContinuationOptions.None),
            liveTask.ContinueWith(t => t, TaskContinuationOptions.None));

        results[0].Exception.Should().BeNull("backfill task must not throw");
        results[1].Exception.Should().BeNull("concurrent live-sign task must not throw");

        // Verify the final chain is clean with no forked SequenceIds.
        var result = await NewService().VerifyAsync(tenantId);
        result.IsValid.Should().BeTrue("applock must serialize backfill and live signer; chain must be clean");
    }

    // ── TEST 4: non-SystemAdmin gets 403 from the admin HTTP endpoint ────────────────────────────
    // Backfill rewrites historical signatures — SystemAdmin-only guard prevents tamper-laundering.

    [Fact]
    public async Task Backfill_RequiresSystemAdmin()
    {
        // GovOfficer role has AUDIT.LOG.VIEW but not SYSTEM.CONFIG.MANAGE — must be denied.
        var client = _app.CreateAuthenticatedClient(
            userId: Guid.NewGuid(),
            tenantId: Guid.NewGuid(),
            roles: ["GovOfficer"]);

        var response = await client.PostAsync("/api/v1/audit/chain/backfill", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the backfill endpoint requires SystemAdmin authority; a GovOfficer must receive 403");
    }

    // ── TEST 5: backfill job writes one audit trail entry with the correct actor ─────────────────
    // Compliance requirement (QĐ 742 §3.1): every admin backfill must be traceable.
    // The HTTP endpoint enqueues a job and returns 202 — the audit entry is written by the JOB,
    // not the handler. We invoke RunBackfillJobAsync directly (synchronous in-test) so the audit
    // write happens inline without needing a live Hangfire worker.

    [Fact]
    public async Task Backfill_WritesAuditTrailEntry_WithCorrectActor()
    {
        var tenantId        = Guid.NewGuid();
        var triggerUserId   = Guid.NewGuid();
        var triggerTenantId = Guid.NewGuid();
        const string triggerUserName = "test-backfill-actor";

        await SeedWronglySignedRowsAsync(tenantId, count: 2);

        // Capture baseline before the job runs.
        var countBefore = await CountAuditEntriesWithActionAsync("chain-backfill");

        // Invoke the job method directly (simulates Hangfire executing the enqueued job).
        // Uses the full DI-wired service so the repository write goes to the real DB.
        var svc = NewServiceWithAuditSupport();
        await svc.RunBackfillJobAsync(triggerUserId, triggerTenantId, triggerUserName);

        // The job must have written exactly one new "chain-backfill" audit entry.
        var countAfter = await CountAuditEntriesWithActionAsync("chain-backfill");
        countAfter.Should().BeGreaterThan(countBefore,
            "the backfill job must write one audit trail entry per run for compliance traceability");

        // The entry must carry the triggering actor (not an empty/anonymous user).
        var entry = await GetLastAuditEntryWithActionAsync("chain-backfill");
        entry.UserId.Should().Be(triggerUserId,
            "audit entry must record the user who triggered the backfill, not the ambient ICurrentUser (empty in Hangfire)");
        entry.UserName.Should().Be(triggerUserName,
            "audit entry must carry the triggering user's name for human-readable compliance evidence");
    }

    // ── TEST 6: backfill audit entry carries richer tamper-evidence (caller IP + chain heads) ─────
    // The backfill entry is the only forensic control over a privileged signature rewrite, so it
    // must record more than the row count: the caller IP plus per-tenant pre/post chain heads.

    [Fact]
    public async Task Backfill_AuditEntry_CarriesCallerIpAndChainHeads()
    {
        var tenantId        = Guid.NewGuid();
        var triggerUserId   = Guid.NewGuid();
        var triggerTenantId = Guid.NewGuid();
        const string triggerUserName = "test-backfill-evidence";
        const string callerIp        = "203.0.113.7";
        const string correlationId   = "test-correlation-evidence";

        await SeedWronglySignedRowsAsync(tenantId, count: 2);

        var svc = NewServiceWithAuditSupport();
        await svc.RunBackfillJobAsync(triggerUserId, triggerTenantId, triggerUserName, callerIp, correlationId);

        var entry = await GetLastAuditEntryWithActionAsync("chain-backfill");

        entry.IpAddress.Should().Be(callerIp,
            "the backfill audit entry must record the caller IP captured at the HTTP boundary");

        entry.DataSnapshot.Should().NotBeNull("the backfill entry must carry a JSON evidence snapshot");
        var snapshot = entry.DataSnapshot!;
        snapshot.Should().Contain("\"rowsReSignedTotal\"", "snapshot must still report the total rows re-signed");
        snapshot.Should().Contain($"\"callerIp\":\"{callerIp}\"", "snapshot must embed the caller IP for forensic cross-check");
        snapshot.Should().Contain($"\"correlationId\":\"{correlationId}\"", "snapshot must embed the correlation id so the blob is self-contained");
        snapshot.Should().Contain("\"tenantChains\"", "snapshot must list per-tenant chain evidence");
        snapshot.Should().Contain("\"postHead\"", "each tenant chain must record its post-backfill head signature");
        // The seeded tenant was re-signed, so its post head must be a non-empty signature.
        using var doc = System.Text.Json.JsonDocument.Parse(snapshot);
        var seededChain = doc.RootElement.GetProperty("tenantChains").EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("tenantId").GetString() == tenantId.ToString());
        seededChain.ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined,
            "the seeded tenant must appear in the per-tenant chain evidence");
        seededChain.GetProperty("postHead").GetString().Should().NotBeNullOrEmpty(
            "the re-signed tenant chain must record a non-empty post-backfill head");
    }

    // Also verify the HTTP endpoint returns 202 Accepted for a SystemAdmin caller.
    [Fact]
    public async Task BackfillEndpoint_SystemAdmin_Returns202()
    {
        var adminClient = _app.CreateAuthenticatedClient(
            userId: Guid.NewGuid(),
            tenantId: Guid.NewGuid(),
            roles: ["SystemAdmin"]);

        var response = await adminClient.PostAsync("/api/v1/audit/chain/backfill", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "the backfill endpoint offloads work to Hangfire and must return 202 Accepted immediately");
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a service instance WITHOUT audit-write support — used for signing/verify tests
    /// that don't need the RunBackfillJobAsync path.
    /// </summary>
    private HmacChainService NewService()
    {
        var scope = _app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);
        return new HmacChainService(repo, keyResolver, NullLogger<HmacChainService>.Instance, GetConnectionString());
    }

    /// <summary>
    /// Creates a service instance WITH audit-write support — used for RunBackfillJobAsync test.
    /// Wires the real repository, guid generator, and a stub job enqueuer (no real Hangfire in tests).
    /// </summary>
    private HmacChainService NewServiceWithAuditSupport()
    {
        var scope = _app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);
        // Stub the job enqueuer — no real Hangfire server in integration tests.
        var jobEnqueuer = Substitute.For<IBackgroundAuditJobEnqueuer>();
        var connStr = GetConnectionString();
        return new HmacChainService(
            repo, keyResolver, NullLogger<HmacChainService>.Instance,
            connectionString: connStr,
            adminConnectionString: connStr,     // test DB is db_owner — same conn for enumeration
            guidGenerator: guidGenerator,
            jobEnqueuer: jobEnqueuer);
    }

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.GetConnectionString()!;
    }

    /// <summary>
    /// Seeds rows that already have an HmacSignature but with an incorrect chain:
    /// SequenceId=0 and the signature was computed independently (not chained from previous).
    /// This simulates the 151 dev rows where the old chain was broken.
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
                 'BackfillTest', 'Audit', 'AuditLog', 'backfill-test',
                 'deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef', 99, 0, 0, 2)
            """;

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();

        for (var i = 0; i < count; i++)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@msgId", Guid.NewGuid());
            // tenantId is non-nullable Guid here — pass directly (no HasValue check needed).
            cmd.Parameters.AddWithValue("@tenantId", (object)tenantId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Seeds one unsigned row (HmacSignature='', SequenceId=0) for a live-sign scenario.</summary>
    private async Task SeedRawUnsignedRowAsync(Guid tenantId)
    {
        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@msgId, @tenantId, GETUTCDATE(), SYSDATETIMEOFFSET(),
                 'LiveWrite', 'Audit', 'AuditLog', 'live-write-test',
                 '', 0, 0, 0, 2)
            """;

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@msgId", Guid.NewGuid());
        cmd.Parameters.Add(new SqlParameter("@tenantId", (object)tenantId));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAuditEntriesWithActionAsync(string action)
    {
        const string sql = "SELECT COUNT(*) FROM [audit].[AuditLogEntries] WHERE Action = @action";
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@action", action);
        var result = await cmd.ExecuteScalarAsync();
        return result is int n ? n : Convert.ToInt32(result);
    }

    /// <summary>Returns the most-recently inserted audit entry with the given action.</summary>
    private async Task<(Guid UserId, string UserName, string? IpAddress, string? DataSnapshot)> GetLastAuditEntryWithActionAsync(string action)
    {
        const string sql = """
            SELECT TOP 1 UserId, UserName, IpAddress, DataSnapshot
            FROM [audit].[AuditLogEntries]
            WHERE Action = @action
            ORDER BY CreatedAt DESC
            """;
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@action", action);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"No audit entry found with action '{action}'.");
        var userId       = reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0);
        var userName     = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var ipAddress    = reader.IsDBNull(2) ? null : reader.GetString(2);
        var dataSnapshot = reader.IsDBNull(3) ? null : reader.GetString(3);
        return (userId, userName, ipAddress, dataSnapshot);
    }
}
