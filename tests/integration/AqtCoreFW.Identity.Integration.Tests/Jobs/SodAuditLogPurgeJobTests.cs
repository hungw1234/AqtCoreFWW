using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Jobs;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Identity.Integration.Tests.Jobs;

/// <summary>
/// Integration tests for SodAuditLogPurgeJob.
/// Runs against real SQL Server (native or Testcontainers) to verify raw SQL + CTE syntax.
///
/// SodViolationLog is T3 append-only:
///   - C# property: InternalId (long, DB-assigned) / CreatedAt — mapped to DB cols Id / DetectedAt
///   - Factory: SodViolationLog.Create(gen, ...) sets CreatedAt = UtcNow
///   - For purge tests, we seed rows via factory then raw-SQL UPDATE DetectedAt to historical value
///   - Identify rows by MessageId (Guid AK) which is available immediately after Create()
///
/// Schema note: DB columns are Id (bigint IDENTITY) and DetectedAt (datetime2) per
/// SodViolationLogConfiguration.HasColumnName mappings.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SodAuditLogPurgeJobTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private readonly IGuidGenerator _guidGen;

    public SodAuditLogPurgeJobTests(WebAppFixture app)
    {
        _app = app;
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
    }

    [Fact]
    public async Task ExecuteAsync_PurgesOldRows_LeavesRecentRows()
    {
        // Arrange: seed 5 rows — 3 older than 30d, 2 recent
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        // Create rows via factory, then backdate DetectedAt (DB col for CreatedAt) via raw SQL
        var oldLogs = new[]
        {
            SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId),
            SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId),
            SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId),
        };
        var recentLogs = new[]
        {
            SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId),
            SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId),
        };

        db.SodViolationLogs.AddRange(oldLogs);
        db.SodViolationLogs.AddRange(recentLogs);
        await db.SaveChangesAsync();

        // Backdate old rows via raw SQL: DetectedAt is the DB column name for CreatedAt property
        var oldDates = new[] {
            DateTime.UtcNow.AddDays(-35),
            DateTime.UtcNow.AddDays(-40),
            DateTime.UtcNow.AddDays(-31),
        };
        for (int i = 0; i < oldLogs.Length; i++)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [identity].[SodViolationLogs] SET DetectedAt = {0} WHERE MessageId = {1}",
                oldDates[i], oldLogs[i].MessageId);
        }

        // Backdate recent rows to within cutoff
        var recentDates = new[] {
            DateTime.UtcNow.AddDays(-29),
            DateTime.UtcNow.AddDays(-5),
        };
        for (int i = 0; i < recentLogs.Length; i++)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [identity].[SodViolationLogs] SET DetectedAt = {0} WHERE MessageId = {1}",
                recentDates[i], recentLogs[i].MessageId);
        }

        // Capture MessageIds for assertion (InternalId is DB-assigned, MessageId is the AK)
        var recentMessageIds = recentLogs.Select(x => x.MessageId).ToList();
        var oldMessageIds = oldLogs.Select(x => x.MessageId).ToList();

        var job = new SodAuditLogPurgeJob(db, NullLogger<SodAuditLogPurgeJob>.Instance);

        // Act
        await job.ExecuteAsync();

        // Assert: only 2 recent rows remain
        var remainingMessageIds = await db.SodViolationLogs
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.MessageId)
            .ToListAsync();

        remainingMessageIds.Should().HaveCount(2);
        remainingMessageIds.Should().Contain(recentMessageIds[0]);
        remainingMessageIds.Should().Contain(recentMessageIds[1]);
        remainingMessageIds.Should().NotIntersectWith(oldMessageIds);
    }

    [Fact]
    public async Task ExecuteAsync_NoOldRows_DoesNothing()
    {
        // Arrange: seed only recent rows
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var recentLog = SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId);
        db.SodViolationLogs.Add(recentLog);
        await db.SaveChangesAsync();

        // Set DetectedAt to 10 days ago (within 30d retention)
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [identity].[SodViolationLogs] SET DetectedAt = {0} WHERE MessageId = {1}",
            DateTime.UtcNow.AddDays(-10), recentLog.MessageId);

        var recentMessageId = recentLog.MessageId;

        var job = new SodAuditLogPurgeJob(db, NullLogger<SodAuditLogPurgeJob>.Instance);

        // Act
        await job.ExecuteAsync();

        // Assert: row still exists
        var exists = await db.SodViolationLogs.AnyAsync(x => x.MessageId == recentMessageId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Idempotent_SecondRunDeletesNothing()
    {
        // Arrange
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var oldLog = SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId);
        var recentLog = SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId);
        db.SodViolationLogs.AddRange(oldLog, recentLog);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [identity].[SodViolationLogs] SET DetectedAt = {0} WHERE MessageId = {1}",
            DateTime.UtcNow.AddDays(-35), oldLog.MessageId);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [identity].[SodViolationLogs] SET DetectedAt = {0} WHERE MessageId = {1}",
            DateTime.UtcNow.AddDays(-5), recentLog.MessageId);

        var job = new SodAuditLogPurgeJob(db, NullLogger<SodAuditLogPurgeJob>.Instance);

        // Act: run twice
        await job.ExecuteAsync();
        var countAfterFirst = await db.SodViolationLogs.CountAsync(x => x.TenantId == tenantId);

        await job.ExecuteAsync();
        var countAfterSecond = await db.SodViolationLogs.CountAsync(x => x.TenantId == tenantId);

        // Assert: count unchanged after second run
        countAfterFirst.Should().Be(1);
        countAfterSecond.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesOldestFirst()
    {
        // Arrange: seed rows with deterministic order to verify oldest-first deletion
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        // Create 3 old rows with distinct timestamps
        var oldest = SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId);
        var middle = SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId);
        var newest = SodViolationLog.Create(_guidGen, tenantId, null, null, null, null, actorId);

        db.SodViolationLogs.AddRange(oldest, middle, newest);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [identity].[SodViolationLogs] SET DetectedAt = {0} WHERE MessageId = {1}",
            DateTime.UtcNow.AddDays(-50), oldest.MessageId);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [identity].[SodViolationLogs] SET DetectedAt = {0} WHERE MessageId = {1}",
            DateTime.UtcNow.AddDays(-40), middle.MessageId);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [identity].[SodViolationLogs] SET DetectedAt = {0} WHERE MessageId = {1}",
            DateTime.UtcNow.AddDays(-35), newest.MessageId);

        var job = new SodAuditLogPurgeJob(db, NullLogger<SodAuditLogPurgeJob>.Instance);

        // Act
        await job.ExecuteAsync();

        // Assert: all 3 old rows purged (they're all >30d old)
        var remaining = await db.SodViolationLogs
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();
        remaining.Should().BeEmpty();
    }
}
