using System.Text.Json;
using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;

namespace AqtCoreFW.MasterData.Application.Queries.WorkflowTaxonomy;

/// <summary>
/// List workflow action taxonomy rows — mirror of state query (no Category/Color).
/// </summary>
public sealed record GetWorkflowActionTaxonomiesQuery(
    int Page = 1,
    int PageSize = 200,
    bool IncludeInactive = false,
    string? Search = null) : IQuery<PagedResult<WorkflowActionTaxonomyDto>>;

public sealed class GetWorkflowActionTaxonomiesQueryHandler(
    IReadDbConnection db,
    IDistributedCache cache)
    : IRequestHandler<GetWorkflowActionTaxonomiesQuery, Result<PagedResult<WorkflowActionTaxonomyDto>>>
{
    public const string CacheKey = "mst:workflow-action-taxonomy:all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private const int DefaultPageSize = 200;

    public async Task<Result<PagedResult<WorkflowActionTaxonomyDto>>> Handle(
        GetWorkflowActionTaxonomiesQuery query, CancellationToken ct)
    {
        // Clamp to PickerMax (1000): FE `useWorkflowActionTaxonomies` passes default pageSize=200
        // (full-set bulk-picker semantics); AdminMax=100 would silently truncate.
        // isDefaultParams uses RAW query.PageSize to preserve cache hit on canonical 200-row request.
        var (page, pageSize) = PaginationDefaults.Normalize(query.Page, query.PageSize, PaginationDefaults.PickerMax);

        var isDefaultParams =
            query.Page == 1 &&
            query.PageSize == DefaultPageSize &&
            !query.IncludeInactive &&
            string.IsNullOrWhiteSpace(query.Search);

        if (isDefaultParams)
        {
            var cached = await cache.GetAsync(CacheKey, ct);
            if (cached is not null && cached.Length > 0)
            {
                var cachedDtos = JsonSerializer.Deserialize<List<WorkflowActionTaxonomyDto>>(cached);
                if (cachedDtos is not null)
                    return Result.Ok(BuildPaged(cachedDtos, page, pageSize));
            }
        }

        var offset = (page - 1) * pageSize;
        var searchPattern = string.IsNullOrWhiteSpace(query.Search) ? null : $"%{query.Search}%";

        // Phase-4b B1 Part 2b: PublicId projection — WorkflowActionTaxonomies.Id is bigint after AMEND-A13.
        const string sql = """
            SELECT a.PublicId AS Id, a.Code, a.NameVi, a.NameEn, a.I18nKey, a.Icon,
                   a.SortOrder, a.IsActive, a.DeprecatedReason, a.RowVersion,
                   COUNT(*) OVER() AS TotalCount
            FROM [masterdata].WorkflowActionTaxonomies a
            WHERE a.IsDeleted = 0
              AND (@IncludeInactive = 1 OR a.IsActive = 1)
              AND (@Search IS NULL
                   OR a.Code LIKE @Search
                   OR a.NameVi LIKE @Search
                   OR a.NameEn LIKE @Search)
            ORDER BY a.SortOrder ASC, a.Code ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = (await db.QueryAsync<WorkflowActionTaxonomyRow>(sql, new
        {
            IncludeInactive = query.IncludeInactive ? 1 : 0,
            Search = searchPattern,
            Offset = offset,
            PageSize = pageSize,
        })).ToList();

        var total = rows.Count > 0 ? rows[0].TotalCount : 0;
        var dtos = rows.Select(r => r.ToDto()).ToList();

        if (isDefaultParams)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(dtos);
            var opts = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl };
            await cache.SetAsync(CacheKey, bytes, opts, ct);
        }

        return Result.Ok(BuildPaged(dtos, page, pageSize, total));
    }

    private static PagedResult<WorkflowActionTaxonomyDto> BuildPaged(
        List<WorkflowActionTaxonomyDto> items,
        int page,
        int pageSize,
        int? total = null)
    {
        var count = total ?? items.Count;
        var totalPages = (int)Math.Ceiling((double)count / pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return new PagedResult<WorkflowActionTaxonomyDto>(items, count, meta);
    }
}

// --- Detail by Id ---

public sealed record GetWorkflowActionTaxonomyByIdQuery(Guid Id)
    : IQuery<WorkflowActionTaxonomyDto>;

public sealed class GetWorkflowActionTaxonomyByIdQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetWorkflowActionTaxonomyByIdQuery, Result<WorkflowActionTaxonomyDto>>
{
    public async Task<Result<WorkflowActionTaxonomyDto>> Handle(
        GetWorkflowActionTaxonomyByIdQuery query, CancellationToken ct)
    {
        // M2: Dapper bypasses EF global filter — exclude hard-deleted
        // Phase-4b B1 Part 2b: ByPublicId — flip WHERE + project PublicId AS Id.
        const string sql = """
            SELECT a.PublicId AS Id, a.Code, a.NameVi, a.NameEn, a.I18nKey, a.Icon,
                   a.SortOrder, a.IsActive, a.DeprecatedReason, a.RowVersion,
                   0 AS TotalCount
            FROM [masterdata].WorkflowActionTaxonomies a
            WHERE a.PublicId = @Id AND a.IsDeleted = 0
            """;

        var row = (await db.QueryAsync<WorkflowActionTaxonomyRow>(sql, new { query.Id })).FirstOrDefault();
        return row is null
            ? Result.Fail($"WorkflowActionTaxonomy with id {query.Id} not found")
            : Result.Ok(row.ToDto());
    }
}

/// <summary>Dapper row class — positional record doesn't work reliably with nullable params. Public for test visibility.</summary>
public sealed class WorkflowActionTaxonomyRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;
    public string NameVi { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public string? I18nKey { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string? DeprecatedReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public int TotalCount { get; set; }

    public WorkflowActionTaxonomyDto ToDto() =>
        new(Id, Code, NameVi, NameEn, I18nKey, Icon,
            SortOrder, IsActive, DeprecatedReason, RowVersion);
}
