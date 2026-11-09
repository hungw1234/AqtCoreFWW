using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Domain;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Application.Queries.GetAuditLogs;

/// <summary>
/// Reads audit logs via Dapper (read-side, bypasses EF).
/// All params use DynamicParameters — no string interpolation (SQLi prevention).
/// Non-SystemAdmin requests always enforce TenantId from ambient context.
/// </summary>
public sealed class GetAuditLogsQueryHandler(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db,
    ITenantContext tenantContext,
    IFeatureFlagService featureFlags,
    ILogger<GetAuditLogsQueryHandler> logger)
    : IRequestHandler<GetAuditLogsQuery, Result<PagedResult<AuditLogDto>>>
{
    public async Task<Result<PagedResult<AuditLogDto>>> Handle(
        GetAuditLogsQuery request,
        CancellationToken cancellationToken)
    {
        // Enforce tenant isolation: non-SystemAdmin MUST filter by ambient TenantId
        var effectiveTenantId = tenantContext.IsSystemAdmin
            ? request.TenantId  // SystemAdmin can optionally filter by any tenant
            : tenantContext.TenantId
              ?? throw new UnauthorizedAccessException("Missing tenant context for audit log access.");

        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var p = new DynamicParameters();
        var where = BuildWhere(request, effectiveTenantId, p);
        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);

        var countSql = $"SELECT COUNT(*) FROM audit.AuditLogEntries {where}";
        var dataSql = $"""
            SELECT MessageId AS Id, TenantId, UserId, UserName, Action, ModuleName,
                   ResourceType, ResourceId, IpAddress, OccurredAt, CorrelationId
            FROM audit.AuditLogEntries
            {where}
            ORDER BY OccurredAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        // RLS: audit_admin_bypass — admin read of system/global audit rows
        // SystemAdmin needs both the COUNT and data SELECT on bypass connections so that
        // NULL-tenant (system event) rows are included in both results.
        var total = tenantContext.IsSystemAdmin
            ? await db.QuerySingleWithAuditAdminBypassAsync<int>(countSql, p, cancellationToken)
            : await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var rows = tenantContext.IsSystemAdmin
            ? await db.QueryWithAuditAdminBypassAsync<AuditLogDto>(dataSql, p, cancellationToken)
            : await db.QueryAsync<AuditLogDto>(dataSql, p, cancellationToken);

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);

        return Result.Ok(new PagedResult<AuditLogDto>(rows.ToList(), total, meta));
    }

    /// <summary>Strip FTS special characters to prevent CONTAINS syntax errors.</summary>
    private static string SanitizeFtsInput(string input) =>
        System.Text.RegularExpressions.Regex.Replace(input, """[\"'\*\~\&\|\!\(\)\[\]\{\}\u201C\u201D]""", "").Trim();

    private string BuildWhere(GetAuditLogsQuery q, Guid? effectiveTenantId, DynamicParameters p)
    {
        var clauses = new List<string>();

        if (effectiveTenantId.HasValue) { clauses.Add("TenantId = @TenantId"); p.Add("TenantId", effectiveTenantId); }
        if (q.UserId.HasValue) { clauses.Add("UserId = @UserId"); p.Add("UserId", q.UserId); }
        if (q.From.HasValue) { clauses.Add("OccurredAt >= @From"); p.Add("From", q.From); }
        if (q.To.HasValue) { clauses.Add("OccurredAt <= @To"); p.Add("To", q.To); }
        if (!string.IsNullOrEmpty(q.Action)) { clauses.Add("Action = @Action"); p.Add("Action", q.Action); }
        if (!string.IsNullOrEmpty(q.ModuleName)) { clauses.Add("ModuleName = @ModuleName"); p.Add("ModuleName", q.ModuleName); }
        if (!string.IsNullOrEmpty(q.ResourceType)) { clauses.Add("ResourceType = @ResourceType"); p.Add("ResourceType", q.ResourceType); }
        if (!string.IsNullOrEmpty(q.ResourceId)) { clauses.Add("ResourceId = @ResourceId"); p.Add("ResourceId", q.ResourceId); }

        // Server-side search: FTS (CONTAINS) with LIKE fallback
        if (!string.IsNullOrWhiteSpace(q.SearchTerm))
        {
            var sanitized = SanitizeFtsInput(q.SearchTerm);
            if (featureFlags.IsEnabled("feature:audit.full-text-search", q.TenantId?.ToString() ?? "")
                && !string.IsNullOrWhiteSpace(sanitized))
            {
                p.Add("SearchTerm", $"\"{sanitized}\"");
                clauses.Add("CONTAINS((Action, ModuleName, ResourceType, UserName), @SearchTerm)");
            }
            else
            {
                var escaped = q.SearchTerm.EscapeSqlLike();
                p.Add("SearchPattern", $"%{escaped}%");
                clauses.Add("(Action LIKE @SearchPattern ESCAPE '\\' OR ModuleName LIKE @SearchPattern ESCAPE '\\' OR ResourceType LIKE @SearchPattern ESCAPE '\\' OR UserName LIKE @SearchPattern ESCAPE '\\')");
                logger.LogDebug("FTS disabled — using LIKE fallback for audit log search");
            }
        }

        return clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
    }
}
