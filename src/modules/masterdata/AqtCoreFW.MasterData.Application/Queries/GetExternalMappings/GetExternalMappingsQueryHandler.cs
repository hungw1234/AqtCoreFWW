using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.MasterData.Application.Queries.GetExternalMappings;

/// <summary>Read-side query via Dapper.</summary>
public sealed class GetExternalMappingsQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetExternalMappingsQuery, Result<PagedResult<ExternalMappingDto>>>
{
    public async Task<Result<PagedResult<ExternalMappingDto>>> Handle(
        GetExternalMappingsQuery query, CancellationToken ct)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(query.Page, query.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;

        // Phase-4b B1 Part 2b: PublicId projection — ExternalMappings.Id (bigint) + DictionaryId (bigint?) FKs.
        var sql = """
            SELECT m.PublicId AS Id, m.InternalCode, m.ExternalSystem, m.ExternalCode,
                   m.Direction,
                   d.PublicId AS DictionaryId,
                   m.IsActive, m.ValidFrom, m.ValidTo, m.TenantId,
                   COUNT(*) OVER() AS TotalCount
            FROM [masterdata].ExternalMappings m
            LEFT JOIN [masterdata].Dictionaries d ON d.Id = m.DictionaryId
            WHERE m.TenantId = @TenantId
              AND m.IsDeleted = 0
              AND (@ExternalSystem IS NULL OR m.ExternalSystem = @ExternalSystem)
              AND (@InternalCode IS NULL OR m.InternalCode = @InternalCode)
              AND (@ActiveOnly = 0 OR m.IsActive = 1)
            ORDER BY m.ExternalSystem ASC, m.InternalCode ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await db.QueryAsync<ExternalMappingRow>(sql, new
        {
            query.TenantId,
            query.ExternalSystem,
            query.InternalCode,
            ActiveOnly = query.ActiveOnly ? 1 : 0,
            Offset = offset,
            PageSize = pageSize
        });

        var list = rows.ToList();
        var total = list.Count > 0 ? list[0].TotalCount : 0;

        var dtos = list.Select(r => new ExternalMappingDto(
            r.Id, r.InternalCode, r.ExternalSystem, r.ExternalCode,
            (MappingDirection)r.Direction, r.DictionaryId,
            r.IsActive, r.ValidFrom, r.ValidTo, r.TenantId)).ToList();

        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<ExternalMappingDto>(dtos, total, meta));
    }
}

internal sealed record ExternalMappingRow(
    Guid Id, string InternalCode, string ExternalSystem, string ExternalCode,
    int Direction, Guid? DictionaryId, bool IsActive,
    DateTime ValidFrom, DateTime? ValidTo, Guid TenantId, int TotalCount);
