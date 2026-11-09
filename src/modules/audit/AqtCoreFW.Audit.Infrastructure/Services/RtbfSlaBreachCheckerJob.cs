using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Infrastructure.Services;

/// <summary>
/// Hangfire recurring job — runs daily at midnight.
/// Scans RtbfRequests that are not yet completed and whose DueBy has passed.
/// Emits a structured warning log per overdue request for compliance dashboards.
/// Does NOT change request status — operators use the existing idempotent
/// ProcessRtbfRequestCommand to complete partial/pending requests.
/// Law 91/2025 Art.9: 30-day SLA from RequestedAt.
/// </summary>
public sealed class RtbfSlaBreachCheckerJob(
    AuditDbContext db,
    ILogger<RtbfSlaBreachCheckerJob> logger)
{
    private static readonly RtbfStatus[] IncompleteStatuses =
    [
        RtbfStatus.Pending,
        RtbfStatus.Processing,
        RtbfStatus.PartiallyCompleted
    ];

    /// <summary>Hangfire entry point — called daily.</summary>
    public async Task ExecuteAsync()
    {
        var now = DateTimeOffset.UtcNow;

        var overdue = await db.RtbfRequests
            .Where(r => IncompleteStatuses.Contains(r.Status) && r.DueBy < now)
            .Select(r => new { r.MessageId, r.TenantId, r.DataSubjectId, r.RequestedAt, r.DueBy, r.Status })
            .ToListAsync();

        if (overdue.Count == 0)
        {
            logger.LogDebug("RtbfSlaChecker: no overdue requests at {Now}", now);
            return;
        }

        foreach (var req in overdue)
        {
            var daysOverdue = (int)(now - req.DueBy).TotalDays;
            logger.LogWarning(
                "RTBF SLA BREACH: RequestId={RequestId} TenantId={TenantId} " +
                "DataSubjectId={DataSubjectId} Status={Status} " +
                "DueBy={DueBy:O} DaysOverdue={Days}",
                req.MessageId, req.TenantId, req.DataSubjectId, req.Status, req.DueBy, daysOverdue);
        }

        logger.LogWarning(
            "RtbfSlaChecker: {Count} overdue RTBF request(s) found at {Now}",
            overdue.Count, now);
    }
}
