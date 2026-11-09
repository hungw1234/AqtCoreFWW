using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job (cron: "0 0 1 */6 *" — every 6 months on 1st day).
/// Creates pending AccessReviewRecord for all active UserRoleAssignments.
/// Uses cursor-based pagination (500 users/batch) to handle large user sets.
/// </summary>
public sealed class AccessReviewSchedulerJob
{
    private const int BatchSize = 500;

    private readonly IdentityDbContext _db;
    private readonly IGuidGenerator _guidGen;
    private readonly ILogger<AccessReviewSchedulerJob> _logger;

    public AccessReviewSchedulerJob(
        IdentityDbContext db,
        IGuidGenerator guidGen,
        ILogger<AccessReviewSchedulerJob> logger)
    {
        _db = db;
        _guidGen = guidGen;
        _logger = logger;
    }

    /// <summary>Hangfire entry point — bulk-creates AccessReviewRecord rows for active role assignments.</summary>
    public async Task ExecuteAsync()
    {
        var nextReviewDue = DateTime.UtcNow.AddMonths(6);
        var createdCount = 0;
        Guid? lastUserId = null;

        _logger.LogInformation("AccessReviewSchedulerJob started. NextReviewDue={Due}", nextReviewDue);

        while (true)
        {
            // Cursor-based pagination on AspNetUserRoles via Identity join
            var batch = await _db.UserRoles
                .AsNoTracking()
                .Where(ur => lastUserId == null || ur.UserId.CompareTo(lastUserId.Value) > 0)
                .OrderBy(ur => ur.UserId)
                .Take(BatchSize)
                .ToListAsync();

            if (batch.Count == 0) break;

            // System actor (Guid.Empty) — job runs without user context
            var records = batch.Select(ur =>
                AccessReviewRecord.Create(_guidGen, ur.UserId, ur.RoleId, nextReviewDue, Guid.Empty))
                .ToList();

            await _db.AccessReviewRecords.AddRangeAsync(records);
            await _db.SaveChangesAsync();

            createdCount += records.Count;
            lastUserId = batch[^1].UserId;

            _logger.LogDebug("AccessReviewScheduler: processed batch, total so far={Count}", createdCount);

            if (batch.Count < BatchSize) break;
        }

        _logger.LogInformation("AccessReviewSchedulerJob completed. Created={Count} records.", createdCount);
    }
}
