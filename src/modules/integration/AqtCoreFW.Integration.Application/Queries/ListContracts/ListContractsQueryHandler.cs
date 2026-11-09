using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Repositories;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Queries.ListContracts;

public sealed class ListContractsQueryHandler(IReadDbConnection db, IPartnerRepository partnerRepository)
    : IRequestHandler<ListContractsQuery, Result<PagedResult<ContractDto>>>
{
    public async Task<Result<PagedResult<ContractDto>>> Handle(
        ListContractsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);

        // Resolve optional T2 PartnerPublicId (Guid AK) → internal long PK for SQL JOIN filter.
        long? partnerInternalId = null;
        if (request.PartnerPublicId.HasValue)
        {
            var partner = await partnerRepository.GetByPublicIdAsync(
                request.PartnerPublicId.Value, request.TenantId, cancellationToken);
            if (partner is null)
                return Result.Ok(new PagedResult<ContractDto>([], 0,
                    new PaginationMeta(page, pageSize, 0, null, null, false)));
            partnerInternalId = partner.Id;
        }

        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);
        p.Add("PartnerInternalId", partnerInternalId);  // long? — resolved from T2 AK
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        var clauses = new List<string>
        {
            "c.TenantId = @TenantId",
            "c.IsDeleted = 0",
            "(@PartnerInternalId IS NULL OR c.PartnerId = @PartnerInternalId)"
        };

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(c.Title LIKE @SearchPattern ESCAPE '\\' OR c.Description LIKE @SearchPattern ESCAPE '\\')");
        }

        var where = string.Join(" AND ", clauses);

        var countSql = $"SELECT COUNT(*) FROM [integration].Contracts c WHERE {where}";

        // T2: SELECT c.PublicId AS Id (Guid AK → ContractDto.Id Guid).
        // PartnerId projected as Guid: JOIN Partners p ON p.Id = c.PartnerId → p.PublicId AS PartnerId.
        var dataSql = $"""
            SELECT c.PublicId AS Id, c.TenantId, p.PublicId AS PartnerId, c.Title, c.Description,
                   c.EffectiveDate, c.ExpiryDate, c.Status, c.DataScopeJson,
                   c.CreatedAt, c.UpdatedAt
            FROM [integration].Contracts c
            JOIN [integration].Partners p ON p.Id = c.PartnerId
            WHERE {where}
            ORDER BY c.EffectiveDate DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var items = (await db.QueryAsync<ContractDto>(dataSql, p, cancellationToken)).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages,
            null, null, page < totalPages);

        return Result.Ok(new PagedResult<ContractDto>(items, total, meta));
    }
}
