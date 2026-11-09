using AqtCoreFW.SystemParams.Application.DTOs;
using AqtCoreFW.SystemParams.Application.Queries;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.SystemParams.Infrastructure.QueryHandlers;

/// <summary>Handler for GetAllSystemParamsQuery — excludes feature: prefix.</summary>
public sealed class GetAllSystemParamsQueryHandler(SystemParamsDbContext db)
    : IRequestHandler<GetAllSystemParamsQuery, Result<IReadOnlyList<SystemParamDto>>>
{
    private const string FeatureFlagPrefix = "feature:";

    public async Task<Result<IReadOnlyList<SystemParamDto>>> Handle(
        GetAllSystemParamsQuery query, CancellationToken ct)
    {
        var tenantId = query.TenantId.ToString();
        var result = await db.SystemParameters.AsNoTracking()
            .Where(p => (p.TenantId == null || p.TenantId == tenantId)
                     && !p.Key.StartsWith(FeatureFlagPrefix))
            .OrderBy(p => p.Key)
            .Select(p => new SystemParamDto(
                p.Key, p.Value, p.DataType.ToString(),
                p.Description, p.IsEditable, p.TenantId))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<SystemParamDto>>(result);
    }
}
