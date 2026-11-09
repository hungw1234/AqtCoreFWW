using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Audit.Application.Queries.GetRtbfRequests;

/// <summary>
/// List RTBF requests — NO cross-schema JOIN.
/// SubjectEmail is denormalized into RtbfRequests table (populated at creation).
/// Uses local columns only — microservice-ready.
/// </summary>
public sealed class GetRtbfRequestsQueryHandler(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db)
    : IRequestHandler<GetRtbfRequestsQuery, Result<PagedResult<RtbfRequestDto>>>
{
    public async Task<Result<PagedResult<RtbfRequestDto>>> Handle(
        GetRtbfRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var p = new DynamicParameters();
        var clauses = new List<string>();

        if (request.TenantId.HasValue) { clauses.Add("TenantId = @TenantId"); p.Add("TenantId", request.TenantId); }
        // Status column is NVARCHAR via HasConversion<string>() — pass enum name, not int.
        if (request.Status.HasValue) { clauses.Add("Status = @Status"); p.Add("Status", request.Status.Value.ToString()); }

        // Search on denormalized SubjectEmail + CitizenNationalId (local columns, no cross-schema JOIN)
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(SubjectEmail LIKE @SearchPattern ESCAPE '\\' OR CitizenNationalId LIKE @SearchPattern ESCAPE '\\')");
        }

        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : string.Empty;
        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);

        var total = await db.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM audit.RtbfRequests {where}", p, cancellationToken);

        // Status column is NVARCHAR (HasConversion<string>) — project directly, no CASE int→string.
        var rows = await db.QueryAsync<RtbfRequestDto>($"""
            SELECT MessageId AS Id, TenantId, DataSubjectId,
                   SubjectEmail AS DataSubjectEmail,
                   CitizenNationalId,
                   Status,
                   RequestedAt, DueBy,
                   ProcessedBy, ProcessedAt, RejectionReason
            FROM audit.RtbfRequests
            {where}
            ORDER BY RequestedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """, p, cancellationToken);

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<RtbfRequestDto>(rows.ToList(), total, meta));
    }
}
