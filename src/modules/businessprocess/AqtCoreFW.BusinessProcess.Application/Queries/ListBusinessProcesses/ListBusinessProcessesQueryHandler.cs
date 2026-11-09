using AqtCoreFW.BusinessProcess.Application.Caching;
using AqtCoreFW.BusinessProcess.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.Queries.ListBusinessProcesses;

/// <summary>
/// Defence-in-depth: handler rejects cross-tenant list (non-SystemAdmin) before any DB call.
/// Returns paged BusinessProcessListItemDto. Reads businessprocess.BusinessProcessDefinitions only
/// (no cross-schema JOINs) — workflow PublicId resolution defers to 2C runtime.
///
/// Slice 2A-Write: cache the "active picker" path only (IsActive=true, no Search, page=1, pageSize=100).
/// That path serves every user opening the BP-Run picker and is the high-RPS hotspot; other shapes
/// (admin filtering, search) are not cached — key-space would explode and writes invalidate by prefix.
/// </summary>
public sealed class ListBusinessProcessesQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    ICacheService cache,
    ILogger<ListBusinessProcessesQueryHandler> logger)
    : IRequestHandler<ListBusinessProcessesQuery, Result<PagedResult<BusinessProcessListItemDto>>>
{
    private static readonly TimeSpan ActiveListTtl = TimeSpan.FromMinutes(5);

    public async Task<Result<PagedResult<BusinessProcessListItemDto>>> Handle(
        ListBusinessProcessesQuery request,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant ListBusinessProcesses denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var cacheKey = TryGetActiveCacheKey(request);
        if (cacheKey is not null)
        {
            var cached = await cache.GetAsync<PagedResult<BusinessProcessListItemDto>>(cacheKey, cancellationToken);
            if (cached is not null)
                return Result.Ok(cached);
        }

        var where = new List<string> { "TenantId = @TenantId", "IsDeleted = 0" };
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);

        if (request.IsActive.HasValue)
        {
            where.Add("IsActive = @IsActive");
            p.Add("IsActive", request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            p.Add("SearchPattern", $"%{request.Search.Replace("%", "[%]")}%");
            where.Add("(Name LIKE @SearchPattern OR Code LIKE @SearchPattern OR Description LIKE @SearchPattern)");
        }

        var whereClause = string.Join(" AND ", where);
        var (page, pageSize) = PaginationDefaults.Normalize(
            request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);

        var countSql = $"SELECT COUNT(*) FROM businessprocess.BusinessProcessDefinitions WHERE {whereClause}";
        var dataSql = $"""
            SELECT PublicId AS Id, Code, Name, Description, WorkflowDefinitionId, IsActive, CreatedAt, UpdatedAt
            FROM businessprocess.BusinessProcessDefinitions
            WHERE {whereClause}
            ORDER BY Code ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var items = (await db.QueryAsync<BusinessProcessListItemDto>(dataSql, p, cancellationToken)).ToList();

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(
            page, pageSize, totalPages,
            NextCursor: null, PrevCursor: null,
            HasNextPage: page < totalPages);

        var result = new PagedResult<BusinessProcessListItemDto>(items, total, meta);

        if (cacheKey is not null)
            await cache.SetAsync(cacheKey, result, ActiveListTtl, cancellationToken);

        return Result.Ok(result);
    }

    /// <summary>
    /// Cache only the user-facing "active picker" shape — controller's <c>/active</c> endpoint
    /// hits this exact signature. Any deviation (search, filtered page, non-default pageSize)
    /// bypasses cache. Key is tenant-scoped to prevent cross-tenant data leaks.
    /// </summary>
    private static string? TryGetActiveCacheKey(ListBusinessProcessesQuery request) =>
        request.IsActive == true
        && string.IsNullOrEmpty(request.Search)
        && request.Page == 1
        && request.PageSize == 100
            ? BusinessProcessCacheKeys.ActiveList(request.TenantId)
            : null;
}
