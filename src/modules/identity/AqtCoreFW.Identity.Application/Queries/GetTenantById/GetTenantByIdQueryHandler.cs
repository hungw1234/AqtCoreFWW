using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.GetTenantById;

public sealed class GetTenantByIdQueryHandler(ITenantRepository tenantRepository)
    : IRequestHandler<GetTenantByIdQuery, Result<TenantDetailDto>>
{
    public async Task<Result<TenantDetailDto>> Handle(GetTenantByIdQuery query, CancellationToken ct)
    {
        // T2: look up by PublicId (Guid AK — external addressing key)
        var tenant = await tenantRepository.GetByPublicIdAsync(query.TenantId, ct);
        if (tenant is null)
        {
            return Result.Fail(new NotFoundError($"Tenant với Id '{query.TenantId}' không tồn tại."));
        }

        var dto = new TenantDetailDto
        {
            Id = tenant.PublicId,   // T2: DTO.Id mapped FROM PublicId (Guid AK)
            Name = tenant.Name,
            Code = tenant.Code,
            ContactEmail = tenant.ContactEmail,
            Status = tenant.Status,
            CreatedAt = tenant.CreatedAt,
            UpdatedAt = tenant.UpdatedAt,
            CreatedBy = tenant.CreatedBy,
            ModifiedBy = tenant.ModifiedBy
        };

        return Result.Ok(dto);
    }
}
