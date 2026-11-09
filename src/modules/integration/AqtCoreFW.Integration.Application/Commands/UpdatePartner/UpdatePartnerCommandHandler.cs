using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Commands.UpdatePartner;

public sealed class UpdatePartnerCommandHandler(
    IPartnerRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<UpdatePartnerCommand, Result<PartnerDto>>
{
    public async Task<Result<PartnerDto>> Handle(
        UpdatePartnerCommand request, CancellationToken cancellationToken)
    {
        // T2 entity: GetByPublicIdAsync takes Guid AK + TenantId (external addressing).
        var partner = await repository.GetByPublicIdAsync(request.PublicId, request.TenantId, cancellationToken);
        if (partner is null)
            return Result.Fail(new NotFoundError($"Partner {request.PublicId} not found."));

        partner.Update(request.Name, request.Code,
            request.ContactEmail, request.ContactPhone,
            request.Endpoint, request.AuthScheme, currentUser.UserId);

        await repository.UpdateAsync(partner, cancellationToken);

        // T2: Id slot = PublicId (Guid AK).
        return Result.Ok(new PartnerDto(
            partner.PublicId, partner.TenantId, partner.Name, partner.Code,
            partner.ContactEmail, partner.ContactPhone, partner.Endpoint, partner.AuthScheme,
            partner.Status.ToString(), partner.CreatedAt, partner.UpdatedAt));
    }
}
