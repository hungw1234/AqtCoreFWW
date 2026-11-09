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
/// Integration tests for the recurring chain-sweep safety net
/// (<see cref="HmacChainService.EnqueueSignForBackloggedChainsAsync"/>).
///
/// Invariant proved: the sweep enqueues an incremental sign for every tenant chain that still has an
/// unsigned (SequenceId=0) row — including the NULL system chain — and skips chains that are already
/// fully signed. This is what closes the dormant-chain gap: a low-traffic chain that stops receiving
/// new appends still gets re-signed without a manual backfill.
///
/// Also swept (not exercised by name here, see HmacChainTailWatermarkTests): a fully-signed chain that
/// is missing its tail watermark row (rollout heal / recovery from a deleted watermark). Because
/// SignTenantChainAsync now writes the watermark in the SAME pass it signs a chain, "signedTenant" and
/// "tenantId" below already carry a fresh watermark by the time the sweep runs — they are correctly
/// NOT re-enqueued for that reason too, not just because they have no unsigned-row backlog. The
/// GUID-scoped, non-exact-count assertions below already tolerate an extra watermark-missing
/// enqueue for chains this test does not own.
///
/// The audit DB is shared across the collection and is NOT reset between tests, so other tests may
/// leave their own backlogged tenants behind. Assertions therefore target only the fresh GUIDs this
/// test owns (deterministic) and never assert an exact total enqueue count.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class HmacChainSweepTests : IClassFixture<WebAppFixture>, IAsyncLifetime
{
    private readonly WebAppFixture _app;

    // NULL-tenant (system chain) rows this test seeds. The system chain is GLOBAL shared state in the
    // collection — any unsigned row left here would be absorbed by a later test's SignTenantChainAsync(null)
    // (e.g. HmacChainPerTenantTests.NullTenantChain_VerifiesIndependently, which asserts an exact count).
    // Delete them in DisposeAsync so this test leaves the system chain as it found it. Named-tenant rows
    // use fresh GUIDs and never collide, so they need no cleanup.
    private readonly List<Guid> _nullRowsToCleanup = new();

    private static readonly byte[] TestKey = new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    };

    public HmacChainSweepTests(WebAppFixture app) => _app = app;

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.GetConnectionString()!;
    }

    private IAuditLogRepository NewRepo() =>
        _app.Services.CreateScope().ServiceProvider.GetRequiredService<IAuditLogRepository>();

    private IGuidGenerator NewGen()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
    }

    /// <summary>Service with a stub key resolver + substitute enqueuer so enqueue calls can be asserted.</summary>
    private HmacChainService NewServiceWithEnqueuer(IBackgroundAuditJobEnqueuer enqueuer)
    {
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);
        var connStr = GetConnectionString();
        // adminConnectionString defaults to connStr (dev sa Default is db_owner) — bypasses the RLS
        // FILTER predicate so the cross-tenant DISTINCT backlog query sees all tenants.
        return new HmacChainService(
            NewRepo(), keyResolver, NullLogger<HmacChainService>.Instance,
            connStr, adminConnectionString: null, guidGenerator: NewGen(), jobEnqueuer: enqueuer);
    }

    private async Task<AuditLogEntry> SeedEntryAsync(Guid? tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var entry = AuditLogEntry.Create(
            gen: gen,
            tenantId: tenantId,
            userId: Guid.NewGuid(),
            userName: "sweep-test",
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
        if (tenantId is null)
            _nullRowsToCleanup.Add(entry.MessageId);
        return entry;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Remove the NULL-tenant rows this test seeded so the shared system chain is left clean for the
    /// next test in the collection. Mirrors the raw-delete + audit_mutate_mode='archive' bypass the
    /// other chain tests use (sa Default is db_owner, so the RLS FILTER predicate is bypassed).
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_nullRowsToCleanup.Count == 0)
            return;

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var ctxCmd = conn.CreateCommand();
        ctxCmd.CommandText = "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'archive', @read_only=0;";
        await ctxCmd.ExecuteNonQueryAsync();

        foreach (var messageId in _nullRowsToCleanup)
        {
            await using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM [audit].[AuditLogEntries] WHERE MessageId = @id;";
            delCmd.Parameters.Add(new SqlParameter("@id", messageId));
            await delCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Sweep_EnqueuesBackloggedChainsAndSkipsSignedOnes()
    {
        var signedTenant = Guid.NewGuid();   // will be signed → no backlog → must NOT be enqueued
        var backloggedTenant = Guid.NewGuid(); // left unsigned → backlog → must be enqueued

        // signedTenant: seed then sign so every row gets SequenceId > 0.
        await SeedEntryAsync(signedTenant);
        var enqueuer = Substitute.For<IBackgroundAuditJobEnqueuer>();
        var svc = NewServiceWithEnqueuer(enqueuer);
        await svc.SignTenantChainAsync(signedTenant);

        // backloggedTenant + the NULL system chain: leave unsigned (SequenceId = 0).
        await SeedEntryAsync(backloggedTenant);
        await SeedEntryAsync(null);

        // Sweep: enqueue sign for every chain with an unsigned backlog.
        var enqueuedCount = await svc.EnqueueSignForBackloggedChainsAsync();

        // The chains this test owns: backlogged ones enqueued, the signed one skipped.
        enqueuer.Received().EnqueueHmacChain(backloggedTenant);
        enqueuer.Received().EnqueueHmacChain((Guid?)null);
        enqueuer.DidNotReceive().EnqueueHmacChain(signedTenant);

        // Count is global (other tests may leave backlog), so only assert it covered our two.
        enqueuedCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Sweep_AfterSigning_DoesNotReEnqueueHealedChain()
    {
        var tenantId = Guid.NewGuid();
        await SeedEntryAsync(tenantId);

        var enqueuer = Substitute.For<IBackgroundAuditJobEnqueuer>();
        var svc = NewServiceWithEnqueuer(enqueuer);

        // Sign it directly so the chain has no SequenceId=0 rows left.
        await svc.SignTenantChainAsync(tenantId);

        await svc.EnqueueSignForBackloggedChainsAsync();

        // A fully-signed chain has no backlog, so the sweep must not enqueue it.
        enqueuer.DidNotReceive().EnqueueHmacChain(tenantId);
    }
}
