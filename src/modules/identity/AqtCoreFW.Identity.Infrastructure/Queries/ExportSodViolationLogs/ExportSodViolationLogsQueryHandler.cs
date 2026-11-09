using AqtCoreFW.Identity.Application.Queries.ExportSodViolationLogs;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Queries.ExportSodViolationLogs;

/// <summary>
/// Handles ExportSodViolationLogsQuery — streaming CSV export of SoD violation logs.
/// Security: inline tenant filter (Identity module has NO global query filter).
/// INNER JOIN to SodConflictRules — V2 soft-delete guarantees rule exists.
/// Streams rows via IAsyncEnumerable, flushes per 500 rows to prevent OOM.
/// </summary>
public sealed class ExportSodViolationLogsQueryHandler(IdentityDbContext db)
    : IRequestHandler<ExportSodViolationLogsQuery, Unit>
{
    private const int FlushInterval = 500;

    public async Task<Unit> Handle(ExportSodViolationLogsQuery request, CancellationToken ct)
    {
        var writer = request.Writer;

        // Write header row
        SodAuditLogCsvWriter.WriteHeader(writer);
        await writer.FlushAsync(ct);

        // Base query with inline tenant filter (P0 multi-tenant security)
        var query = db.SodViolationLogs
            .Where(v => v.TenantId == request.TenantId);

        // Optional filters
        if (request.UserId.HasValue)
            query = query.Where(v => v.UserId == request.UserId.Value);

        if (request.RoleId.HasValue)
            query = query.Where(v => v.RoleId == request.RoleId.Value);

        if (request.RuleId.HasValue)
            query = query.Where(v => v.SodRuleId == request.RuleId.Value);

        if (request.From.HasValue)
            query = query.Where(v => v.CreatedAt >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(v => v.CreatedAt <= request.To.Value);

        // INNER JOIN to SodConflictRules (rules always exist per V2 soft-delete)
        // Deterministic order: CreatedAt DESC, InternalId (tie-break)
        var rows = query
            .Join(
                db.SodConflictRules,
                v => v.SodRuleId,
                r => (long?)r.Id,
                (v, r) => new
                {
                    DetectedAt = v.CreatedAt,
                    Id = v.InternalId,
                    v.UserId,
                    v.RoleId,
                    v.SodRuleId,
                    r.PermissionCodeA,
                    r.PermissionCodeB,
                    Severity = r.Severity.ToString(),
                    v.ActorId,
                    v.Justification
                })
            .OrderByDescending(x => x.DetectedAt)
            .ThenByDescending(x => x.Id)
            .AsAsyncEnumerable();

        var rowCount = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            SodAuditLogCsvWriter.WriteRow(
                writer,
                row.DetectedAt,
                row.UserId,
                userName: null,  // UserName not stored on log — lookup would be expensive
                row.RoleId,
                roleName: null,  // RoleName not stored on log
                row.SodRuleId,
                row.PermissionCodeA,
                row.PermissionCodeB,
                row.Severity,
                row.ActorId,
                actorName: null, // ActorName not stored on log
                row.Justification);

            rowCount++;
            if (rowCount % FlushInterval == 0)
            {
                await writer.FlushAsync(ct);
            }
        }

        // Final flush
        await writer.FlushAsync(ct);
        return Unit.Value;
    }
}
