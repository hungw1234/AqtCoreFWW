using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.GetExternalIdentityById;

public sealed class GetExternalIdentityByIdQueryHandler(
    IExternalIdentityRepository repository,
    IReadDbConnection db)
    : IRequestHandler<GetExternalIdentityByIdQuery, Result<ExternalIdentityDto>>
{
    public async Task<Result<ExternalIdentityDto>> Handle(
        GetExternalIdentityByIdQuery query, CancellationToken ct)
    {
        var entity = await repository.GetByIdAsync(query.Id, ct);
        if (entity is null)
            return Result.Fail<ExternalIdentityDto>(
                new NotFoundError($"ExternalIdentity {query.Id} not found"));

        // Tenant isolation: the bigint Id is a sequential IDENTITY, so an un-scoped fetch
        // is bulk-enumerable. ExternalIdentities has no TenantId column / no RLS — bind a
        // non-privileged caller to links whose owning user lives in their tenant. Return
        // NotFound (not Forbidden) on a cross-tenant hit so record existence is not leaked.
        if (!query.IsPrivileged)
        {
            var ownedByTenant = await db.QuerySingleAsync<int>(
                "SELECT COUNT(1) FROM [identity].AspNetUsers WHERE Id = @UserId AND TenantId = @TenantId",
                new { entity.UserId, TenantId = query.CallerTenantId },
                ct);
            if (ownedByTenant == 0)
                return Result.Fail<ExternalIdentityDto>(
                    new NotFoundError($"ExternalIdentity {query.Id} not found"));
        }

        // T1: DTO.Id mapped from entity.Id (long IDENTITY)
        return Result.Ok(new ExternalIdentityDto(
            entity.Id, entity.UserId, entity.Provider,
            entity.ExternalId, entity.DisplayName, entity.Email,
            entity.LinkedAt, entity.LastSyncAt, entity.IsActive));
    }
}
