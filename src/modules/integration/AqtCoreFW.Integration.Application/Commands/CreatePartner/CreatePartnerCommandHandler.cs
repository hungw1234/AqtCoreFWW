using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Commands.CreatePartner;

public sealed class CreatePartnerCommandHandler(
    IPartnerRepository repository,
    IGuidGenerator guidGenerator,
    ICurrentUser currentUser,
    IDomainEventPublisher eventPublisher)
    : IRequestHandler<CreatePartnerCommand, Result<PartnerDto>>
{
    public async Task<Result<PartnerDto>> Handle(
        CreatePartnerCommand request, CancellationToken cancellationToken)
    {
        var partner = Partner.Create(
            guidGenerator, request.TenantId, request.Name, request.Code, currentUser.UserId,
            request.ContactEmail, request.ContactPhone,
            request.Endpoint, request.AuthScheme);

        await repository.AddAsync(partner, cancellationToken);
        await eventPublisher.PublishAsync(partner.DomainEvents, cancellationToken);
        partner.ClearDomainEvents();

        return Result.Ok(MapToDto(partner));
    }

    // T2 entity: Id slot = PublicId (Guid AK — set at Create time by IGuidGenerator).
    private static PartnerDto MapToDto(Partner p) => new(
        p.PublicId, p.TenantId, p.Name, p.Code,
        p.ContactEmail, p.ContactPhone, p.Endpoint, p.AuthScheme,
        p.Status.ToString(), p.CreatedAt, p.UpdatedAt);
}
