using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Audit.Application.Queries.GetAiPromptTraces;

/// <summary>
/// Reads AI prompt traces via Dapper (read-side).
/// PromptText is excluded from SELECT — sensitive field never exposed via query.
/// </summary>
public sealed class GetAiPromptTracesQueryHandler(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db)
    : IRequestHandler<GetAiPromptTracesQuery, Result<PagedResult<AiPromptTraceDto>>>
{
    public async Task<Result<PagedResult<AiPromptTraceDto>>> Handle(
        GetAiPromptTracesQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        var clauses = new List<string> { "TenantId = @TenantId" };

        // Server-side search: LIKE on ModelName and ClassificationLevel
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(ModelName LIKE @SearchPattern ESCAPE '\\' OR ClassificationLevel LIKE @SearchPattern ESCAPE '\\')");
        }

        var where = "WHERE " + string.Join(" AND ", clauses);

        var countSql = $"SELECT COUNT(*) FROM audit.AiPromptTraces {where}";
        var dataSql = $"""
            SELECT MessageId AS Id, SessionId, ModelProfileId, ModelName, PromptHash,
                   ResponseHash, InputTokens, OutputTokens, LatencyMs,
                   Cost, ClassificationLevel, TenantId, CreatedAt
            FROM audit.AiPromptTraces
            {where}
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var rows = await db.QueryAsync<AiPromptTraceDto>(dataSql, p, cancellationToken);

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);

        return Result.Ok(new PagedResult<AiPromptTraceDto>(rows.ToList(), total, meta));
    }
}
