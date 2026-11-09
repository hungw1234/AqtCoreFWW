using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.MasterData.Application.Queries.GetDictionaries;

/// <summary>Read-side query via Dapper — follows project read-side patterns.</summary>
public sealed class GetDictionariesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetDictionariesQuery, Result<PagedResult<DictionaryDto>>>
{
    public async Task<Result<PagedResult<DictionaryDto>>> Handle(
        GetDictionariesQuery query, CancellationToken ct)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(query.Page, query.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;

        // Phase-4b B1 Part 2b: project PublicId AS Id (column Dictionaries.Id is bigint after AMEND-A13 re-tier).
        var sql = """
            SELECT d.PublicId AS Id, d.Code, d.Name, d.NameVi, d.Description,
                   d.Status, d.CurrentVersion, d.TenantId, d.IsSystemDefined,
                   COUNT(*) OVER() AS TotalCount
            FROM [masterdata].Dictionaries d
            WHERE d.TenantId = @TenantId
              AND d.IsDeleted = 0
              AND (@Status IS NULL OR d.Status = @Status)
              AND (@Search IS NULL OR d.Name LIKE @Search ESCAPE '\'
                                   OR d.Code LIKE @Search ESCAPE '\')
            ORDER BY d.Code ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await db.QueryAsync<DictionaryRow>(sql, new
        {
            query.TenantId,
            Status = (int?)query.Status,
            Search = query.Search is not null ? $"%{query.Search}%" : null,
            Offset = offset,
            PageSize = pageSize
        });

        var list = rows.ToList();
        var total = list.Count > 0 ? list[0].TotalCount : 0;

        var dtos = list.Select(r => new DictionaryDto(
            r.Id, r.Code, r.Name, r.NameVi, r.Description,
            (DictionaryStatus)r.Status, r.CurrentVersion, r.TenantId, r.IsSystemDefined)).ToList();

        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<DictionaryDto>(dtos, total, meta));
    }
}

internal sealed record DictionaryRow(
    Guid Id, string Code, string Name, string NameVi, string? Description,
    int Status, int CurrentVersion, Guid TenantId, bool IsSystemDefined, int TotalCount);
