using AqtCoreFW.MasterData.Application.Queries.GetCaseTypes;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.MasterData.Infrastructure.QueryHandlers;

/// <summary>Handler for GetCaseTypesQuery — fetches active case types for tenant (+ global).</summary>
public sealed class GetCaseTypesQueryHandler(MasterDataDbContext db)
    : IRequestHandler<GetCaseTypesQuery, Result<IReadOnlyList<MasterDataItemDto>>>
{
    public async Task<Result<IReadOnlyList<MasterDataItemDto>>> Handle(
        GetCaseTypesQuery query, CancellationToken ct)
    {
        var tid = query.TenantId?.ToString();
        var result = await db.CaseTypes.AsNoTracking()
            .Where(c => c.IsActive && (c.TenantId == null || c.TenantId == tid))
            .OrderBy(c => c.SortOrder)
            .Select(c => new MasterDataItemDto(c.Code, c.NameVi, c.NameEn))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<MasterDataItemDto>>(result);
    }
}
