using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Integration.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Commands.DeletePartner;

public sealed class DeletePartnerCommandHandler(IPartnerRepository repository)
    : IRequestHandler<DeletePartnerCommand, Result>
{
    public async Task<Result> Handle(
        DeletePartnerCommand request, CancellationToken cancellationToken)
    {
        // T2 entity: GetByPublicIdAsync takes Guid AK + TenantId (external addressing).
        var partner = await repository.GetByPublicIdAsync(request.PublicId, request.TenantId, cancellationToken);
        if (partner is null)
            return Result.Fail(new NotFoundError($"Partner {request.PublicId} not found."));

        partner.Delete();
        await repository.UpdateAsync(partner, cancellationToken);
        return Result.Ok();
    }
}
