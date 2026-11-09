using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Services;
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
/// Proves that per-tenant sp_getapplock serializes concurrent SignTenantChainAsync calls for the
/// same tenant, while different tenants run in parallel.
///
/// WHY sp_getapplock (not [DisableConcurrentExecution]):
///   Hangfire's [DisableConcurrentExecution] locks by {TypeName}.{MethodName} — it has no
///   awareness of method arguments. Two jobs calling SignTenantChainAsync(tenantA) and
///   SignTenantChainAsync(tenantA) would both proceed concurrently, racing on the chain state.
///   sp_getapplock @Resource='audit-hmac:{tenantKey}' is keyed per-tenant, so same-tenant
///   jobs serialize while different-tenant jobs (different lock resources) run in parallel.
///
/// Test design:
///   - ConcurrentSameTenant_Serialized_NoFork: two tasks sign the same tenant simultaneously.
///     If the lock works, one waits for the other; the chain is contiguous (no SequenceId fork).
///   - ConcurrentDifferentTenants_RunInParallel: two tasks sign different tenants; both must
///     complete without dead-locking (different lock resources, no contention).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class HmacChainConcurrencyTests : IClassFixture<WebAppFixture>
{
    private static readonly byte[] TestKey =
    [
        0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8,
        0xA9, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xAF, 0xB0,
        0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8,
        0xB9, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0
    ];

    private readonly WebAppFixture _app;

    public HmacChainConcurrencyTests(WebAppFixture app) => _app = app;

    // ── TEST 1: same tenant — concurrent signers serialize (no chain fork) ────
    // A "chain fork" would occur if both signers read the same anchor simultaneously,
    // then both compute overlapping SequenceIds. Rows would end up with duplicate
    // SequenceIds or a broken chain. sp_getapplock prevents this by serializing
    // SignTenantChainAsync calls that share the same tenantKey.

    [Fact]
    public async Task ConcurrentSameTenant_Serialized_NoFork()
    {
        var tenantId = Guid.NewGuid();

        // Seed four entries. Two will be signed by each of the two concurrent signers.
        await SeedEntriesAsync(tenantId, count: 4);

        var connStr = GetConnectionString();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Launch two concurrent signing tasks on the same tenant.
        var task1 = Task.Run(() => NewService(connStr).SignTenantChainAsync(tenantId, cts.Token), cts.Token);
        var task2 = Task.Run(() => NewService(connStr).SignTenantChainAsync(tenantId, cts.Token), cts.Token);

        // Both must complete without exception (the lock must be released properly).
        var results = await Task.WhenAll(
            task1.ContinueWith(t => t, TaskContinuationOptions.None),
            task2.ContinueWith(t => t, TaskContinuationOptions.None));

        results[0].Exception.Should().BeNull("first concurrent signer must not throw");
        results[1].Exception.Should().BeNull("second concurrent signer must not throw");

        // ALL four entries must be signed — no row left behind.
        var rows = await ReadAllSignedAsync(tenantId);
        rows.Should().HaveCount(4, "both signers together must sign all four rows");

        // Verify SequenceIds are contiguous 1..4 — no forks, no gaps.
        var seqs = rows.Select(r => r.SequenceId).OrderBy(s => s).ToList();
        seqs.Should().Equal([1L, 2L, 3L, 4L],
            "SequenceIds must be contiguous (1..4) with no forks from concurrent signers");

        // Verify each signature is non-empty and unique.
        rows.Select(r => r.Signature).Should().OnlyContain(s => !string.IsNullOrEmpty(s),
            "every row must have a non-empty HMAC signature");
        rows.Select(r => r.Signature).Distinct().Should().HaveCount(4,
            "each row's HMAC is unique (chained hash)");
    }

    // ── TEST 2: different tenants — concurrent signers run in parallel ─────────
    // Each tenant has its own applock resource key ('audit-hmac:{tenantId}'), so the two
    // signers acquire different locks and can proceed simultaneously. If they dead-locked
    // (wrong lock scope, wrong LockOwner, etc.) the Task.WhenAll would time out.

    [Fact]
    public async Task ConcurrentDifferentTenants_RunInParallel()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedEntriesAsync(tenantA, count: 2);
        await SeedEntriesAsync(tenantB, count: 2);

        var connStr = GetConnectionString();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Both tasks must complete well within the total timeout.
        await Task.WhenAll(
            Task.Run(() => NewService(connStr).SignTenantChainAsync(tenantA, cts.Token), cts.Token),
            Task.Run(() => NewService(connStr).SignTenantChainAsync(tenantB, cts.Token), cts.Token));

        sw.Stop();

        // Both tenants must be fully signed.
        (await ReadAllSignedAsync(tenantA)).Should().HaveCount(2,
            "tenant A rows must be signed when running concurrently with tenant B");
        (await ReadAllSignedAsync(tenantB)).Should().HaveCount(2,
            "tenant B rows must be signed when running concurrently with tenant A");

        // A rough parallelism check: if the lock is scoped per-tenant (correct), both signers
        // run concurrently. If somehow they serialise across tenants, the elapsed time would
        // roughly double — but we cannot enforce a strict timing bound in CI. We assert both
        // completed successfully, which is sufficient to prove no deadlock or excessive blocking.
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(25),
            "two different-tenant signers must not dead-lock or contend excessively");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private HmacChainService NewService(string connectionString)
    {
        var keyResolver = Substitute.For<IAuditHmacKeyResolver>();
        keyResolver.GetKey().Returns(TestKey);

        // HmacChainService(connStr, keyResolver, logger) — Phase 2 raw-conn ctor.
        // RED until the Phase 2 implementation adds this constructor.
        return new HmacChainService(connectionString, keyResolver, NullLogger<HmacChainService>.Instance);
    }

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.GetConnectionString()!;
    }

    private async Task SeedEntriesAsync(Guid? tenantId, int count)
    {
        using var scope = _app.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@msgId, @tenantId, GETUTCDATE(), SYSDATETIMEOFFSET(),
                 'ConcurrencyTest', 'Tests', 'AuditLog', 'concurrent-signer-test',
                 '', 0, 0, 0, 2)
            """;

        var connStr = db.Database.GetConnectionString()!;
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        for (var i = 0; i < count; i++)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@msgId", Guid.NewGuid());
            cmd.Parameters.Add(new SqlParameter("@tenantId",
                tenantId.HasValue ? tenantId.Value : DBNull.Value));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<List<(long SequenceId, string Signature)>> ReadAllSignedAsync(Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        const string sql = """
            SELECT SequenceId, HmacSignature
            FROM [audit].[AuditLogEntries]
            WHERE TenantId = @tenantId
              AND SequenceId > 0
              AND HmacSignature != ''
            ORDER BY SequenceId
            """;

        var connStr = db.Database.GetConnectionString()!;
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);

        var result = new List<(long, string)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((reader.GetInt64(0), reader.GetString(1)));
        return result;
    }
}
