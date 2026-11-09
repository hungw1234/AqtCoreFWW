using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SystemParams.Application.Commands;
using AqtCoreFW.SystemParams.Application.DTOs;
using AqtCoreFW.SystemParams.Application.Services;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.SystemParams.Infrastructure.CommandHandlers;

/// <summary>Handler for UpdateSystemParamCommand — blocks feature: prefix.</summary>
public sealed class UpdateSystemParamCommandHandler(
    SystemParamsDbContext db,
    ISystemParamService paramService)
    : IRequestHandler<UpdateSystemParamCommand, Result<UpdateSystemParamResult>>
{
    private const string FeatureFlagPrefix = "feature:";

    public async Task<Result<UpdateSystemParamResult>> Handle(
        UpdateSystemParamCommand cmd, CancellationToken ct)
    {
        var normalizedKey = cmd.Key.ToLowerInvariant();
        if (normalizedKey.StartsWith(FeatureFlagPrefix, StringComparison.Ordinal))
            return Result.Fail(new ValidationError(
                $"Key '{normalizedKey}' is a feature flag — use PUT /api/v1/admin/feature-flags/{{flagName}} so L0 cache invalidation propagates."));

        var tenantId = cmd.TenantId.ToString();
        var param = await db.SystemParameters
            .FirstOrDefaultAsync(p => p.Key == normalizedKey && p.TenantId == tenantId, ct);

        if (param is null)
            return Result.Fail(new NotFoundError($"Parameter '{normalizedKey}' not found."));

        try
        {
            param.UpdateValue(cmd.Value);
            await db.SaveChangesAsync(ct);
            await paramService.SetAsync(normalizedKey, cmd.Value, tenantId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(new ValidationError(ex.Message));
        }

        return Result.Ok(new UpdateSystemParamResult(param.Key, param.Value));
    }
}
