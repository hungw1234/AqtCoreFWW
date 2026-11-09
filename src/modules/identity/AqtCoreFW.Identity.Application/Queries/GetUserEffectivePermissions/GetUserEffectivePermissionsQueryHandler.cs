using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.GetUserEffectivePermissions;

/// <summary>
/// Resolves the effective permission summary from Redis cache (or recomputes from DB).
/// Delegates to IEffectivePermissionService — no direct DB access in this handler.
/// </summary>
public sealed class GetUserEffectivePermissionsQueryHandler(
    IEffectivePermissionService permissionService)
    : IRequestHandler<GetUserEffectivePermissionsQuery, Result<EffectivePermissionSummary>>
{
    public async Task<Result<EffectivePermissionSummary>> Handle(
        GetUserEffectivePermissionsQuery request, CancellationToken ct)
    {
        if (request.UserId == Guid.Empty)
            return Result.Fail(new ValidationError("UserId must not be empty."));

        var summary = await permissionService.GetSummaryAsync(request.UserId, ct);
        return Result.Ok(summary);
    }
}
