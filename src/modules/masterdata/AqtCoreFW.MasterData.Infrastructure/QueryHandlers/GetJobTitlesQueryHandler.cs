using AqtCoreFW.MasterData.Application.Queries.GetCaseTypes;
using AqtCoreFW.MasterData.Application.Queries.GetJobTitles;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.MasterData.Infrastructure.QueryHandlers;

/// <summary>Handler for GetJobTitlesQuery — fetches active job titles for tenant (+ global).</summary>
public sealed class GetJobTitlesQueryHandler(MasterDataDbContext db)
    : IRequestHandler<GetJobTitlesQuery, Result<IReadOnlyList<MasterDataItemDto>>>
{
    public async Task<Result<IReadOnlyList<MasterDataItemDto>>> Handle(
        GetJobTitlesQuery query, CancellationToken ct)
    {
        var tid = query.TenantId?.ToString();
        var result = await db.JobTitles.AsNoTracking()
            .Where(j => j.IsActive && (j.TenantId == null || j.TenantId == tid))
            .OrderBy(j => j.SortOrder)
            .Select(j => new MasterDataItemDto(j.Code, j.NameVi, j.NameEn))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<MasterDataItemDto>>(result);
    }
}
