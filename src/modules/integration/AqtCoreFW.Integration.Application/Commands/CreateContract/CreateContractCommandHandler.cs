using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Commands.CreateContract;

/// <summary>
/// Phase-07 PR-B: Resolves PartnerPublicId (Guid T2 AK) → internal long Id via IPartnerRepository.GetByPublicIdAsync.
/// Contract.PartnerId (long FK) stays internal — only the AK is exposed externally.
/// </summary>
public sealed class CreateContractCommandHandler(
    IContractRepository repository,
    IPartnerRepository partnerRepository,
    ICurrentUser currentUser,
    IGuidGenerator guidGenerator)
    : IRequestHandler<CreateContractCommand, Result<ContractDto>>
{
    public async Task<Result<ContractDto>> Handle(
        CreateContractCommand request, CancellationToken cancellationToken)
    {
        // Resolve T2 PartnerPublicId (Guid AK) → internal long PK for FK storage.
        var partner = await partnerRepository.GetByPublicIdAsync(
            request.PartnerPublicId, request.TenantId, cancellationToken);
        if (partner is null)
            return Result.Fail<ContractDto>("Partner not found");

        // T2 entity: factory takes IGuidGenerator to assign PublicId AK.
        var contract = Contract.Create(
            guidGenerator, request.TenantId, partner.Id,
            request.Title, request.Description,
            request.EffectiveDate, request.ExpiryDate,
            request.DataScopeJson, currentUser.UserId);

        await repository.AddAsync(contract, cancellationToken);

        // T2: Id slot = PublicId (Guid AK), PartnerId = Partner.PublicId (Guid AK).
        return Result.Ok(new ContractDto(
            contract.PublicId, contract.TenantId, partner.PublicId,
            contract.Title, contract.Description,
            contract.EffectiveDate, contract.ExpiryDate,
            contract.Status.ToString(), contract.DataScopeJson,
            contract.CreatedAt, contract.UpdatedAt));
    }
}
