using AqtCoreFW.Identity.Application.Queries.ListSodViolationLogs;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Queries.ListSodViolationLogs;

/// <summary>
/// Handles ListSodViolationLogsQuery — paged list of SoD violation audit logs.
/// Security: inline tenant filter (Identity module has NO global query filter).
/// INNER JOIN to SodConflictRules — V2 soft-delete guarantees rule exists for any log with SodRuleId.
/// </summary>
public sealed class ListSodViolationLogsQueryHandler(IdentityDbContext db)
    : IRequestHandler<ListSodViolationLogsQuery, Result<ListSodViolationLogsQueryResult>>
{
    public async Task<Result<ListSodViolationLogsQueryResult>> Handle(
        ListSodViolationLogsQuery request, CancellationToken ct)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(
            request.Page, request.PageSize, PaginationDefaults.AdminMax);

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

        // Count before pagination
        var totalCount = await query.CountAsync(ct);

        // INNER JOIN to SodConflictRules — V2 soft-delete-only guarantees rule exists
        // Order by CreatedAt DESC using index IX_SodViolationLogs_TenantId_DetectedAt
        var items = await query
            .Join(
                db.SodConflictRules,
                v => v.SodRuleId,
                r => (long?)r.Id,
                (v, r) => new SodViolationLogDto(
                    v.MessageId,
                    v.CreatedAt,
                    v.UserId,
                    v.RoleId,
                    v.SodRuleId,
                    r.PermissionCodeA,
                    r.PermissionCodeB,
                    r.Severity.ToString(),
                    v.ActorId,
                    v.Justification))
            .OrderByDescending(dto => dto.DetectedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Result.Ok(new ListSodViolationLogsQueryResult(items, totalCount, page, pageSize));
    }
}
