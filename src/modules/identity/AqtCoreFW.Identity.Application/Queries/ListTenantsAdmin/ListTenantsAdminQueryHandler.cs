using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListTenantsAdmin;

public sealed class ListTenantsAdminQueryHandler(
    ITenantRepository tenantRepository,
    IReadDbConnection db)
    : IRequestHandler<ListTenantsAdminQuery, Result<PagedResult<TenantAdminDto>>>
{
    public async Task<Result<PagedResult<TenantAdminDto>>> Handle(
        ListTenantsAdminQuery query, CancellationToken ct)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(query.Page, query.PageSize, PaginationDefaults.AdminMax);

        // Get paginated tenants from repository
        var (tenants, totalCount) = await tenantRepository.ListAsync(
            query.Search,
            query.Status,
            page,
            pageSize,
            ct);

        if (tenants.Count == 0)
        {
            var emptyMeta = new PaginationMeta(page, pageSize, 0, null, null, false);
            return Result.Ok(new PagedResult<TenantAdminDto>([], 0, emptyMeta));
        }

        // T2: DTO.Id mapped FROM PublicId (Guid AK — external addressing key)
        var dtos = tenants.Select(t => new TenantAdminDto
        {
            Id = t.PublicId,
            Name = t.Name,
            Code = t.Code,
            ContactEmail = t.ContactEmail,
            Status = t.Status,
            CreatedAt = t.CreatedAt,
            UserCount = 0 // Will be enriched below
        }).ToList();

        // Enrich with user counts via Dapper.
        // AspNetUsers.TenantId stores Tenant.PublicId (Guid AK) — matches dto.Id above.
        // [identity] schema must be bracket-escaped (T-SQL reserved keyword)
        var tenantPublicIds = tenants.Select(t => t.PublicId).ToArray();
        const string sql = """
            SELECT TenantId, COUNT(*) AS UserCount
            FROM [identity].[AspNetUsers]
            WHERE TenantId IN @TenantIds
            GROUP BY TenantId
            """;

        var userCounts = await db.QueryAsync<TenantUserCountRow>(sql, new { TenantIds = tenantPublicIds }, ct);
        var countDict = userCounts.ToDictionary(x => x.TenantId, x => x.UserCount);

        foreach (var dto in dtos)
        {
            if (countDict.TryGetValue(dto.Id, out var count))
            {
                dto.UserCount = count;
            }
        }

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        var hasNextPage = page < totalPages;
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, hasNextPage);
        return Result.Ok(new PagedResult<TenantAdminDto>(dtos, totalCount, meta));
    }

}

// Class-based DTO for Dapper compatibility (memory: feedback-dapper-no-positional-records.md).
// Public so test doubles (NSubstitute) can proxy IEnumerable<TenantUserCountRow>.
public sealed class TenantUserCountRow
{
    public Guid TenantId { get; init; }
    public int UserCount { get; init; }
}
