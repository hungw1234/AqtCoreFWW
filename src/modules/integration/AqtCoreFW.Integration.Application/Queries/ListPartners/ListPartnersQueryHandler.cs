using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.Integration.Application.DTOs;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Queries.ListPartners;

public sealed class ListPartnersQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListPartnersQuery, Result<PagedResult<PartnerDto>>>
{
    public async Task<Result<PagedResult<PartnerDto>>> Handle(
        ListPartnersQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        var clauses = new List<string>
        {
            "TenantId = @TenantId",
            "IsDeleted = 0"
        };

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(Name LIKE @SearchPattern ESCAPE '\\' OR Code LIKE @SearchPattern ESCAPE '\\' OR ContactEmail LIKE @SearchPattern ESCAPE '\\')");
        }

        var where = string.Join(" AND ", clauses);

        var countSql = $"SELECT COUNT(*) FROM [integration].Partners WHERE {where}";

        // T2: SELECT PublicId AS Id (Guid AK → PartnerDto.Id Guid).
        var dataSql = $"""
            SELECT PublicId AS Id, TenantId, Name, Code, ContactEmail, ContactPhone,
                   Endpoint, AuthScheme, Status, CreatedAt, UpdatedAt
            FROM [integration].Partners
            WHERE {where}
            ORDER BY Name
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var items = (await db.QueryAsync<PartnerDto>(dataSql, p, cancellationToken)).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages,
            null, null, page < totalPages);

        return Result.Ok(new PagedResult<PartnerDto>(items, total, meta));
    }
}
