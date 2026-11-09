using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Audit.Application.Queries.GetSecurityIncidents;

public sealed class GetSecurityIncidentsQueryHandler(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db)
    : IRequestHandler<GetSecurityIncidentsQuery, Result<PagedResult<SecurityIncidentDto>>>
{
    public async Task<Result<PagedResult<SecurityIncidentDto>>> Handle(
        GetSecurityIncidentsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var p = new DynamicParameters();
        var clauses = new List<string>();

        if (request.TenantId.HasValue) { clauses.Add("TenantId = @TenantId"); p.Add("TenantId", request.TenantId); }
        if (request.Severity.HasValue) { clauses.Add("Severity = @Severity"); p.Add("Severity", (int)request.Severity); }
        if (request.Status.HasValue) { clauses.Add("Status = @Status"); p.Add("Status", request.Status.ToString()); }
        if (request.From.HasValue) { clauses.Add("OccurredAt >= @From"); p.Add("From", request.From); }
        if (request.To.HasValue) { clauses.Add("OccurredAt <= @To"); p.Add("To", request.To); }

        // Server-side search: LIKE on Title, Description, ReportedBy
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(Title LIKE @SearchPattern ESCAPE '\\' OR Description LIKE @SearchPattern ESCAPE '\\' OR ReportedBy LIKE @SearchPattern ESCAPE '\\')");
        }

        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : string.Empty;
        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);

        var total = await db.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM audit.SecurityIncidents {where}", p, cancellationToken);

        var rows = await db.QueryAsync<SecurityIncidentDto>($"""
            SELECT MessageId AS Id, TenantId, Title, Severity, Status, Description, ReportedBy,
                   OccurredAt, ResolvedAt, Mitigations, CreatedAt
            FROM audit.SecurityIncidents {where}
            ORDER BY OccurredAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """, p, cancellationToken);

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<SecurityIncidentDto>(rows.ToList(), total, meta));
    }
}
