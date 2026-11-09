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
/// List workflow state taxonomy rows — paged, optional filter.
/// Cache: only default-params (page=1, pageSize=200, includeInactive=false, no search) hits
/// Redis with 15min TTL per plan D5; filtered/searched requests always DB direct.
/// </summary>
public sealed record GetWorkflowStateTaxonomiesQuery(
    int Page = 1,
    int PageSize = 200,
    bool IncludeInactive = false,
    string? Search = null) : IQuery<PagedResult<WorkflowStateTaxonomyDto>>;

public sealed class GetWorkflowStateTaxonomiesQueryHandler(
    IReadDbConnection db,
    IDistributedCache cache)
    : IRequestHandler<GetWorkflowStateTaxonomiesQuery, Result<PagedResult<WorkflowStateTaxonomyDto>>>
{
    public const string CacheKey = "mst:workflow-state-taxonomy:all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private const int DefaultPageSize = 200;

    public async Task<Result<PagedResult<WorkflowStateTaxonomyDto>>> Handle(
        GetWorkflowStateTaxonomiesQuery query, CancellationToken ct)
    {
        // Clamp to PickerMax (1000): FE `useWorkflowStateTaxonomies` passes default pageSize=200
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
                var cachedDtos = JsonSerializer.Deserialize<List<WorkflowStateTaxonomyDto>>(cached);
                if (cachedDtos is not null)
                    return Result.Ok(BuildPaged(cachedDtos, page, pageSize));
            }
        }

        var offset = (page - 1) * pageSize;
        var searchPattern = string.IsNullOrWhiteSpace(query.Search) ? null : $"%{query.Search}%";

        // Phase-4b B1 Part 2b: PublicId projection — WorkflowStateTaxonomies.Id is bigint after AMEND-A13.
        const string sql = """
            SELECT s.PublicId AS Id, s.Code, s.NameVi, s.NameEn, s.I18nKey, s.Icon, s.Color,
                   s.Category, s.SortOrder, s.IsActive, s.DeprecatedReason, s.RowVersion,
                   COUNT(*) OVER() AS TotalCount
            FROM [masterdata].WorkflowStateTaxonomies s
            WHERE s.IsDeleted = 0
              AND (@IncludeInactive = 1 OR s.IsActive = 1)
              AND (@Search IS NULL
                   OR s.Code LIKE @Search
                   OR s.NameVi LIKE @Search
                   OR s.NameEn LIKE @Search)
            ORDER BY s.SortOrder ASC, s.Code ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = (await db.QueryAsync<WorkflowStateTaxonomyRow>(sql, new
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

    private static PagedResult<WorkflowStateTaxonomyDto> BuildPaged(
        List<WorkflowStateTaxonomyDto> items,
        int page,
        int pageSize,
        int? total = null)
    {
        var count = total ?? items.Count;
        var totalPages = (int)Math.Ceiling((double)count / pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return new PagedResult<WorkflowStateTaxonomyDto>(items, count, meta);
    }
}

// --- Detail by Id ---

public sealed record GetWorkflowStateTaxonomyByIdQuery(Guid Id)
    : IQuery<WorkflowStateTaxonomyDto>;

public sealed class GetWorkflowStateTaxonomyByIdQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetWorkflowStateTaxonomyByIdQuery, Result<WorkflowStateTaxonomyDto>>
{
    public async Task<Result<WorkflowStateTaxonomyDto>> Handle(
        GetWorkflowStateTaxonomyByIdQuery query, CancellationToken ct)
    {
        // M2: Dapper bypasses EF global filter — must explicitly exclude hard-deleted rows
        // Phase-4b B1 Part 2b: ByPublicId — flip WHERE + project PublicId AS Id.
        const string sql = """
            SELECT s.PublicId AS Id, s.Code, s.NameVi, s.NameEn, s.I18nKey, s.Icon, s.Color,
                   s.Category, s.SortOrder, s.IsActive, s.DeprecatedReason, s.RowVersion,
                   0 AS TotalCount
            FROM [masterdata].WorkflowStateTaxonomies s
            WHERE s.PublicId = @Id AND s.IsDeleted = 0
            """;

        var row = (await db.QueryAsync<WorkflowStateTaxonomyRow>(sql, new { query.Id })).FirstOrDefault();
        return row is null
            ? Result.Fail($"WorkflowStateTaxonomy with id {query.Id} not found")
            : Result.Ok(row.ToDto());
    }
}

/// <summary>Dapper row class (mutable) — required per memory feedback (Dapper fails on positional records with nullable params). Public for test visibility.</summary>
public sealed class WorkflowStateTaxonomyRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;
    public string NameVi { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public string? I18nKey { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string Category { get; set; } = default!;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string? DeprecatedReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public int TotalCount { get; set; }

    public WorkflowStateTaxonomyDto ToDto() =>
        new(Id, Code, NameVi, NameEn, I18nKey, Icon, Color,
            Category, SortOrder, IsActive, DeprecatedReason, RowVersion);
}
