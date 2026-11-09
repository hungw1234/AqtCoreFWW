using AqtCoreFW.SystemParams.Application.DTOs;
using AqtCoreFW.SystemParams.Application.Queries;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.SystemParams.Infrastructure.QueryHandlers;

/// <summary>Handler for GetAllFeatureFlagsQuery — only feature: prefix.</summary>
public sealed class GetAllFeatureFlagsQueryHandler(SystemParamsDbContext db)
    : IRequestHandler<GetAllFeatureFlagsQuery, Result<IReadOnlyList<FeatureFlagDto>>>
{
    private const string FeatureFlagPrefix = "feature:";

    public async Task<Result<IReadOnlyList<FeatureFlagDto>>> Handle(
        GetAllFeatureFlagsQuery query, CancellationToken ct)
    {
        var result = await db.SystemParameters.AsNoTracking()
            .Where(p => p.Key.StartsWith(FeatureFlagPrefix))
            .OrderBy(p => p.Key)
            .Select(p => new FeatureFlagDto(
                p.Key.Substring(FeatureFlagPrefix.Length),
                p.Key,
                p.Value == "true",
                p.Description,
                p.TenantId))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<FeatureFlagDto>>(result);
    }
}
