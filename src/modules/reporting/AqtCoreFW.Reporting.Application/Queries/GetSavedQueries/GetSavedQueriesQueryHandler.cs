using AqtCoreFW.Reporting.Application.Queries.GetSavedQueries;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetSavedQueries;

public sealed class GetSavedQueriesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetSavedQueriesQuery, Result<PagedResult<QueryCatalogEntryDto>>>
{
    public async Task<Result<PagedResult<QueryCatalogEntryDto>>> Handle(
        GetSavedQueriesQuery request,
        CancellationToken cancellationToken)
    {
        // Filter by RBAC: return entries with no RequiredRole, or matching caller's role
        var sql = """
            SELECT Id, Code, Description, DescriptionVi, RequiredParams, OptionalParams, RequiredRole
            FROM reporting.QueryCatalogEntries
            WHERE IsDeleted = 0 AND IsActive = 1
              AND (RequiredRole IS NULL OR RequiredRole = @CallerRole)
            ORDER BY Code
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var countSql = """
            SELECT COUNT(*)
            FROM reporting.QueryCatalogEntries
            WHERE IsDeleted = 0 AND IsActive = 1
              AND (RequiredRole IS NULL OR RequiredRole = @CallerRole)
            """;

        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;
        var p = new { request.CallerRole, Offset = offset, PageSize = pageSize };

        var items = (await db.QueryAsync<QueryCatalogEntryDto>(sql, p, cancellationToken)).ToList();
        var total = await db.QuerySingleAsync<int>(countSql, new { request.CallerRole }, cancellationToken);
        var totalPages = (int)Math.Ceiling((double)total / pageSize);

        var meta = new PaginationMeta(
            Page: page,
            PageSize: pageSize,
            TotalPages: totalPages,
            NextCursor: null,
            PrevCursor: null,
            HasNextPage: page < totalPages);

        return Result.Ok(new PagedResult<QueryCatalogEntryDto>(items, total, meta));
    }
}
