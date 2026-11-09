using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.Audit.Infrastructure.Jobs;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
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
/// Integration tests for the durable audit-ingestion recovery path (cell #7 L1). Exercises
/// <see cref="AuditIngestionRetryJob"/> against the real audit DB through the same
/// <see cref="AuditEntryPersister"/> AuditService uses, proving a failed synchronous audit write is
/// recoverable (not lost), idempotent on MessageId, and safe against the F-H window (row already
/// committed, only the HMAC enqueue was missed).
///
/// The job is constructed manually with a SUBSTITUTE <see cref="IBackgroundAuditJobEnqueuer"/> so the
/// test needs no running Hangfire server (mirrors HmacChainTailWatermarkTests.NewServiceWithEnqueuer);
/// all other dependencies are the real scoped services. Fresh GUIDs per test → no cross-test collision.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class AuditIngestionRecoveryTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public AuditIngestionRecoveryTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task Recover_InsertsRow_WithFrozenAmbientState()
    {
        var tenantId = Guid.NewGuid();
        var intent = BuildIntent(tenantId);
        var (job, enqueuer) = NewJob();

        await job.ExecuteAsync(intent);

        var row = await ReadRowAsync(intent.MessageId);
        row.Should().NotBeNull("the retry job must durably insert the failed audit row");
        row!.Value.TenantId.Should().Be(tenantId, "the recovered row must carry the frozen tenant — NOT re-derive it from a (null) background principal");
        row.Value.UserName.Should().Be(intent.UserName);
        row.Value.IpAddress.Should().Be(intent.IpAddress);
        row.Value.CorrelationId.Should().Be(intent.CorrelationId);
        row.Value.Action.Should().Be(intent.Action);

        // The recovered row must be handed to the per-tenant HMAC signer, same as the sync path.
        enqueuer.Received().EnqueueHmacChain(tenantId);
    }

    [Fact]
    public async Task Recover_RunTwice_YieldsExactlyOneRow()
    {
        var tenantId = Guid.NewGuid();
        var intent = BuildIntent(tenantId);

        // Each Hangfire run gets a FRESH DI scope (fresh AuditDbContext) — a fresh job per execution
        // mirrors that. The second run's INSERT then reaches SQL and the AK constraint raises the
        // unique-key violation the job treats as idempotent success.
        var (job1, _) = NewJob();
        await job1.ExecuteAsync(intent);

        var (job2, _) = NewJob();
        await job2.ExecuteAsync(intent); // Hangfire automatic-retry replay

        (await CountRowsAsync(intent.MessageId)).Should().Be(1, "dedup on MessageId must make replays idempotent");
    }

    [Fact]
    public async Task Recover_RowAlreadyPresent_NoDuplicate_ReenqueuesChainSign()
    {
        // F-H window: the sync persist committed the row but a LATER step (HMAC enqueue) threw, so the
        // fallback fired with a MessageId that already exists. The job must NOT insert a duplicate; it
        // must re-drive the missed chain sign.
        var tenantId = Guid.NewGuid();
        var intent = BuildIntent(tenantId);
        await SeedExistingRowAsync(intent);

        var (job, enqueuer) = NewJob();
        await job.ExecuteAsync(intent);

        (await CountRowsAsync(intent.MessageId)).Should().Be(1, "an already-present MessageId must not be re-inserted");
        enqueuer.Received().EnqueueHmacChain(tenantId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private (AuditIngestionRetryJob job, IBackgroundAuditJobEnqueuer enqueuer) NewJob()
    {
        var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<IAuditLogRepository>();
        var uow = sp.GetRequiredService<ICrossModuleUnitOfWork>();
        var outbox = sp.GetRequiredService<IOutboxWriter>();
        var gen = sp.GetRequiredService<IGuidGenerator>();
        var enqueuer = Substitute.For<IBackgroundAuditJobEnqueuer>();

        var persister = new AuditEntryPersister(repo, uow, outbox, enqueuer, gen);
        var job = new AuditIngestionRetryJob(persister, enqueuer, NullLogger<AuditIngestionRetryJob>.Instance);
        return (job, enqueuer);
    }

    private static AuditIngestionIntent BuildIntent(Guid tenantId) => new()
    {
        MessageId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        UserName = "recovery-test",
        Action = "Create",
        ModuleName = "Forms",
        ResourceType = "Template",
        ResourceId = Guid.NewGuid().ToString(),
        DataSnapshot = null,
        IpAddress = "203.0.113.9",
        CorrelationId = "corr-" + Guid.NewGuid().ToString("N"),
        OccurredAt = DateTimeOffset.UtcNow,
        Category = AuditCategory.AppAccess,
        Severity = AuditSeverity.Low,
        SchemaVersion = 2
    };

    /// <summary>Pre-inserts the row (unsigned) so the job's dedup branch is exercised (F-H).</summary>
    private async Task SeedExistingRowAsync(AuditIngestionIntent intent)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        db.AuditLogEntries.Add(intent.ToEntry());
        await db.SaveChangesAsync();
    }

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.GetConnectionString()!;
    }

    private async Task<(Guid? TenantId, string UserName, string? IpAddress, string? CorrelationId, string Action)?>
        ReadRowAsync(Guid messageId)
    {
        const string sql = """
            SELECT TenantId, UserName, IpAddress, CorrelationId, Action
            FROM [audit].[AuditLogEntries] WHERE MessageId = @m
            """;
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@m", messageId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return (
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4));
    }

    private async Task<int> CountRowsAsync(Guid messageId)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM [audit].[AuditLogEntries] WHERE MessageId = @m", conn);
        cmd.Parameters.AddWithValue("@m", messageId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
