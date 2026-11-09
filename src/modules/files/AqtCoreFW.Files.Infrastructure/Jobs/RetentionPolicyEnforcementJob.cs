using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Files.Infrastructure.Jobs;

/// <summary>
/// Hangfire job: enforces retention policies by finding expired records and
/// archiving them or marking them for destruction.
/// Runs daily at 02:00 (registered in InfrastructureRegistration).
/// Checks ArchiveAfterDays and DestroyAfterDays thresholds against file upload date.
/// [DisableConcurrentExecution] prevents overlapping runs during large tenant scans.
///
/// Phase-4b B3b: RecordLifecycle is T3 plain-class. FK types flipped:
///   FileRecordId: Guid → long (FileRecord.Id bigint).
///   PolicyId: Guid → long (RetentionPolicy.Id bigint).
///   LifecycleId: Guid → long (RecordLifecycle.InternalId bigint PK).
///   RecordLifecycle.Create now requires IGuidGenerator first arg.
///   Archive()/ScheduleDestruction(DateTime) take no extra actor arg.
/// </summary>
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(3600)]
public sealed class RetentionPolicyEnforcementJob(
    IReadDbConnection db,
    FilesDbContext ctx,
    IGuidGenerator guidGen,
    ILogger<RetentionPolicyEnforcementJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await ArchiveExpiredRecordsAsync(ct);
        await MarkForDestructionAsync(ct);
    }

    /// <summary>Archive records that have exceeded ArchiveAfterDays threshold.</summary>
    private async Task ArchiveExpiredRecordsAsync(CancellationToken ct)
    {
        // Phase-4b B3b: FileRecords.Id and RetentionPolicies.Id are bigint; RecordLifecycles.Id → InternalId bigint.
        // l.InternalId IS NULL check (T3 PK column renamed from Id).
        const string sql = """
            SELECT f.Id AS FileRecordId, p.ArchiveAfterDays, p.Id AS PolicyId
            FROM files.FileRecords f
            INNER JOIN files.RetentionPolicies p
                ON p.TenantId = f.TenantId AND p.IsActive = 1 AND p.DocumentType = f.ContentType
            LEFT JOIN files.RecordLifecycles l ON l.FileRecordId = f.Id
            WHERE f.IsDeleted = 0
              AND p.ArchiveAfterDays IS NOT NULL
              AND DATEDIFF(DAY, f.CreatedAt, GETUTCDATE()) >= p.ArchiveAfterDays
              AND (l.InternalId IS NULL OR l.CurrentStatus = 0)
            """;

        var records = (await db.QueryAsync<ExpiredRow>(sql, cancellationToken: ct)).ToList();

        if (records.Count == 0)
            return;

        logger.LogInformation("RetentionPolicyEnforcementJob: archiving {Count} records.", records.Count);

        foreach (var row in records)
        {
            // Check if lifecycle record already exists by long FK
            var lifecycle = await ctx.RecordLifecycles
                .FirstOrDefaultAsync(l => l.FileRecordId == row.FileRecordId, ct);

            if (lifecycle is null)
            {
                lifecycle = RecordLifecycle.Create(guidGen, row.FileRecordId, row.PolicyId);
                lifecycle.Archive();
                await ctx.RecordLifecycles.AddAsync(lifecycle, ct);
            }
            else
            {
                lifecycle.Archive();
            }
        }

        await ctx.SaveChangesAsync(ct);
        logger.LogInformation("RetentionPolicyEnforcementJob: archived {Count} records.", records.Count);
    }

    /// <summary>Mark archived records for destruction when DestroyAfterDays threshold reached.</summary>
    private async Task MarkForDestructionAsync(CancellationToken ct)
    {
        // Phase-4b B3b: l.InternalId is the bigint PK for FindAsync lookup.
        const string sql = """
            SELECT l.InternalId AS LifecycleId, l.FileRecordId,
                   p.DestroyAfterDays,
                   DATEDIFF(DAY, f.CreatedAt, GETUTCDATE()) AS AgeDays
            FROM files.RecordLifecycles l
            INNER JOIN files.FileRecords f ON f.Id = l.FileRecordId
            INNER JOIN files.RetentionPolicies p ON p.Id = l.RetentionPolicyId
            WHERE l.CurrentStatus = 1
              AND p.DestroyAfterDays IS NOT NULL
              AND DATEDIFF(DAY, f.CreatedAt, GETUTCDATE()) >= p.DestroyAfterDays
              AND l.ScheduledDestroyAt IS NULL
              AND f.IsDeleted = 0
            """;

        var records = (await db.QueryAsync<DestructionRow>(sql, cancellationToken: ct)).ToList();

        if (records.Count == 0)
            return;

        logger.LogInformation(
            "RetentionPolicyEnforcementJob: scheduling destruction for {Count} records.", records.Count);

        foreach (var row in records)
        {
            // FindAsync uses InternalId (long PK) for T3 RecordLifecycle
            var lifecycle = await ctx.RecordLifecycles.FindAsync([row.LifecycleId], ct);
            lifecycle?.ScheduleDestruction(DateTime.UtcNow.AddDays(7));
        }

        await ctx.SaveChangesAsync(ct);
    }

    // Phase-4b B3b: FK types are now long (bigint identity PKs after T1/T2 re-tier).
    private sealed record ExpiredRow(
        long FileRecordId, int ArchiveAfterDays, long PolicyId);

    private sealed record DestructionRow(
        long LifecycleId, long FileRecordId,
        int DestroyAfterDays, int AgeDays);
}
