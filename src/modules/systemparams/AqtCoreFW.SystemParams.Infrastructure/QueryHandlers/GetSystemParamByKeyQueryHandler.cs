using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SystemParams.Application.DTOs;
using AqtCoreFW.SystemParams.Application.Queries;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.SystemParams.Infrastructure.QueryHandlers;

/// <summary>Handler for GetSystemParamByKeyQuery — blocks feature: prefix.</summary>
public sealed class GetSystemParamByKeyQueryHandler(SystemParamsDbContext db)
    : IRequestHandler<GetSystemParamByKeyQuery, Result<SystemParamDto>>
{
    private const string FeatureFlagPrefix = "feature:";

    public async Task<Result<SystemParamDto>> Handle(
        GetSystemParamByKeyQuery query, CancellationToken ct)
    {
        var normalizedKey = query.Key.ToLowerInvariant();
        if (normalizedKey.StartsWith(FeatureFlagPrefix, StringComparison.Ordinal))
            return Result.Fail(new NotFoundError($"Parameter '{normalizedKey}' not found."));

        var tenantId = query.TenantId.ToString();
        var param = await db.SystemParameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == normalizedKey && p.TenantId == tenantId, ct);

        if (param is null)
            return Result.Fail(new NotFoundError($"Parameter '{normalizedKey}' not found."));

        return Result.Ok(new SystemParamDto(
            param.Key, param.Value, param.DataType.ToString(),
            param.Description, param.IsEditable, param.TenantId));
    }
}
