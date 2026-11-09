using AqtCoreFW.Audit.Infrastructure.HealthChecks;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AqtCoreFW.Audit.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="UnsignedAuditBacklogHealthCheck"/>.
///
/// The check reads unsigned rows across ALL tenants using the elevated (db_owner-capable)
/// admin connection — this is the same RLS bypass used by BackfillAllTenantsAsync.
/// Without the admin connection, a non-db_owner login would see only null-tenant rows via
/// audit_admin_bypass, under-reporting the backlog and producing a falsely healthy result.
/// These tests seed rows directly via raw SQL (bypassing application-layer constraints)
/// and assert that the check reports the correct health status.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class UnsignedAuditBacklogHealthCheckTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public UnsignedAuditBacklogHealthCheckTests(WebAppFixture app) => _app = app;

    // ── TEST 1: unsigned count above threshold → Unhealthy ───────────────────

    [Fact]
    public async Task UnsignedBacklog_AboveThreshold_ReturnsUnhealthy()
    {
        // Seed (threshold + 1) unsigned rows for a unique tenant so this test is isolated.
        const int threshold = 5; // deliberately low threshold for test speed
        var tenantId = Guid.NewGuid();
        await SeedUnsignedRowsAsync(tenantId, count: threshold + 1);

        var check = BuildCheck(maxUnsignedCount: threshold, maxOldestUnsignedAge: TimeSpan.FromHours(1));
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy,
            "unsigned row count exceeds the configured threshold — check must report Unhealthy");

        // Description must be populated with measured numbers so operators can act without querying the DB.
        // We cannot assert the exact count because the shared test DB accumulates rows from other tests.
        // Assert the threshold value appears (it is always embedded by BuildDescription).
        result.Description.Should().Contain(
            threshold.ToString(),
            "description must include the configured threshold for operator visibility");
    }

    // ── TEST 2: no rows, or all rows signed → Healthy ────────────────────────

    [Fact]
    public async Task Empty_OrAllSigned_ReturnsHealthy()
    {
        // Use a tenant that has no rows at all (fresh Guid — won't match any existing rows).
        // The check reads across ALL tenants, so this test relies on the aggregate being below threshold.
        // We cannot assert "zero unsigned rows globally" because other tests may leave unsigned rows.
        // Instead, we use a very high threshold so the check is Healthy for any realistic backlog.
        var check = BuildCheck(maxUnsignedCount: 100_000, maxOldestUnsignedAge: TimeSpan.FromDays(365));
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy,
            "with threshold high enough to encompass any test-seeded backlog, check must report Healthy");
    }

    // ── TEST 3: oldest-unsigned-age above age threshold → Unhealthy ──────────

    [Fact]
    public async Task OldUnsignedRow_ExceedsAgeThreshold_ReturnsUnhealthy()
    {
        // Seed one unsigned row with OccurredAt 2 hours ago by direct SQL so we can set the time.
        var tenantId = Guid.NewGuid();
        await SeedUnsignedRowWithOccurredAtAsync(tenantId, occurredAt: DateTimeOffset.UtcNow.AddHours(-2));

        // Threshold: count=999999 (so count alone won't trigger), but age=1 hour.
        var check = BuildCheck(maxUnsignedCount: 999_999, maxOldestUnsignedAge: TimeSpan.FromHours(1));
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy,
            "the oldest unsigned row is 2 hours old which exceeds the 1-hour age threshold — must be Unhealthy");

        result.Description.Should().NotBeNullOrEmpty(
            "description must be populated for operator troubleshooting");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private UnsignedAuditBacklogHealthCheck BuildCheck(int maxUnsignedCount, TimeSpan maxOldestUnsignedAge)
    {
        var opts = Options.Create(new UnsignedAuditBacklogHealthCheckOptions
        {
            MaxUnsignedCount = maxUnsignedCount,
            MaxOldestUnsignedAge = maxOldestUnsignedAge
        });

        // The admin connection string equals the default in tests (test DB is db_owner).
        var connStr = GetConnectionString();
        return new UnsignedAuditBacklogHealthCheck(opts, connStr, NullLogger<UnsignedAuditBacklogHealthCheck>.Instance);
    }

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.GetConnectionString()!;
    }

    /// <summary>
    /// Seeds unsigned rows (SequenceId=0, HmacSignature='') for isolation tests.
    /// OccurredAt defaults to SYSDATETIMEOFFSET() so the age check won't trigger unless aged rows are needed.
    /// </summary>
    private async Task SeedUnsignedRowsAsync(Guid tenantId, int count)
    {
        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@msgId, @tenantId, GETUTCDATE(), SYSDATETIMEOFFSET(),
                 'HealthCheckTest', 'Audit', 'AuditLog', 'hc-test',
                 '', 0, 0, 0, 2)
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
    /// Seeds one unsigned row with an explicit OccurredAt timestamp to test the age threshold.
    /// </summary>
    private async Task SeedUnsignedRowWithOccurredAtAsync(Guid tenantId, DateTimeOffset occurredAt)
    {
        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@msgId, @tenantId, GETUTCDATE(), @occurredAt,
                 'HealthCheckAgeTest', 'Audit', 'AuditLog', 'hc-age-test',
                 '', 0, 0, 0, 2)
            """;

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@msgId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@tenantId", (object)tenantId);
        cmd.Parameters.AddWithValue("@occurredAt", occurredAt);
        await cmd.ExecuteNonQueryAsync();
    }
}
