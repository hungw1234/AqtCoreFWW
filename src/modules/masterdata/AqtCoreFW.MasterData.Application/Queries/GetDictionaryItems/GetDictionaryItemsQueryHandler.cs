using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.MasterData.Application.Queries.GetDictionaryItems;

/// <summary>
/// Returns flat list of DictionaryItems ordered by SortOrder.
/// Clients build the tree using ParentId — avoids recursive CTE complexity in read queries.
/// </summary>
public sealed class GetDictionaryItemsQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetDictionaryItemsQuery, Result<PagedResult<DictionaryItemDto>>>
{
    public async Task<Result<PagedResult<DictionaryItemDto>>> Handle(
        GetDictionaryItemsQuery query, CancellationToken ct)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(query.Page, query.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;

        // Phase-4b B1 Part 2b: PublicId projection — DictionaryItems.Id, DictionaryId, ParentId are all
        // bigint internal FKs after AMEND-A13. Filter by parent Dictionary.PublicId; expose siblings' PublicIds.
        var sql = """
            SELECT i.PublicId AS Id,
                   d.PublicId AS DictionaryId,
                   i.Code, i.Name, i.NameVi,
                   p.PublicId AS ParentId,
                   i.SortOrder, i.EffectiveFrom, i.EffectiveTo, i.IsActive,
                   COUNT(*) OVER() AS TotalCount
            FROM [masterdata].DictionaryItems i
            INNER JOIN [masterdata].Dictionaries d ON d.Id = i.DictionaryId
            LEFT JOIN [masterdata].DictionaryItems p ON p.Id = i.ParentId
            WHERE d.PublicId = @DictionaryId
              AND i.IsDeleted = 0
              AND (@ActiveOnly = 0 OR i.IsActive = 1)
            ORDER BY i.SortOrder ASC, i.Code ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await db.QueryAsync<DictionaryItemRow>(sql, new
        {
            query.DictionaryId,
            ActiveOnly = query.ActiveOnly ? 1 : 0,
            Offset = offset,
            PageSize = pageSize
        });

        var list = rows.ToList();
        var total = list.Count > 0 ? list[0].TotalCount : 0;

        var dtos = list.Select(r => new DictionaryItemDto(
            r.Id, r.DictionaryId, r.Code, r.Name, r.NameVi,
            r.ParentId, r.SortOrder, r.EffectiveFrom, r.EffectiveTo, r.IsActive)).ToList();

        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<DictionaryItemDto>(dtos, total, meta));
    }
}

internal sealed record DictionaryItemRow(
    Guid Id, Guid DictionaryId, string Code, string Name, string NameVi,
    Guid? ParentId, int SortOrder, DateTime EffectiveFrom,
    DateTime? EffectiveTo, bool IsActive, int TotalCount);
