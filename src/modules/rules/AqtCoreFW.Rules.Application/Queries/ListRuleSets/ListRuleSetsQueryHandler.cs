using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Application.Queries.ListRuleSets;

public sealed class ListRuleSetsQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    ILogger<ListRuleSetsQueryHandler> logger)
    : IRequestHandler<ListRuleSetsQuery, Result<PagedResult<RuleSetListItemDto>>>
{
    public async Task<Result<PagedResult<RuleSetListItemDto>>> Handle(
        ListRuleSetsQuery request,
        CancellationToken cancellationToken)
    {
        // Defence-in-depth: reject cross-tenant RuleSet list.
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant ListRuleSets denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var where = new List<string> { "TenantId = @TenantId", "IsDeleted = 0" };
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            p.Add("SearchPattern", $"%{request.Search.Replace("%", "[%]")}%");
            where.Add("(Name LIKE @SearchPattern OR Description LIKE @SearchPattern)");
        }

        // PickerMax (1000) — FE rule-set-list-page does client-side search across the full set.
        // RuleSet is a config entity (tens typical); 1000-cap gives 10x headroom.
        // FE was coordinated to drop the `pageSize=9999` literal to 1000 in the same FU.
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.PickerMax);

        var whereClause = string.Join(" AND ", where);
        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);

        var countSql = $"SELECT COUNT(*) FROM rules.RuleSets WHERE {whereClause}";
        // T2 RuleSet: project PublicId AS Id (external addressing).
        var dataSql = $"""
            SELECT PublicId AS Id, Name, Description, Version, Status, CreatedAt, UpdatedAt
            FROM rules.RuleSets
            WHERE {whereClause}
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var items = (await db.QueryAsync<RuleSetListItemDto>(dataSql, p, cancellationToken)).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(
            page, pageSize, totalPages,
            NextCursor: null, PrevCursor: null,
            HasNextPage: page < totalPages);

        return Result.Ok(new PagedResult<RuleSetListItemDto>(items, total, meta));
    }
}
