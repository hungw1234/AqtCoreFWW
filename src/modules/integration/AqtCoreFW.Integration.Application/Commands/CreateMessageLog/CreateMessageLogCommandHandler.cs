using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Commands.CreateMessageLog;

/// <summary>
/// Phase-07 PR-B: Resolves PartnerPublicId (Guid T2 AK) → internal long Id via IPartnerRepository.GetByPublicIdAsync.
/// Resolves ContractPublicId (Guid? T2 AK) → internal long? Id via IContractRepository.GetByPublicIdAsync.
/// Both repos scoped to TenantId — cross-tenant isolation enforced at repo signature level.
/// </summary>
public sealed class CreateMessageLogCommandHandler(
    IMessageLogRepository repository,
    IPartnerRepository partnerRepository,
    IContractRepository contractRepository,
    ICurrentUser currentUser,
    IGuidGenerator guidGenerator)
    : IRequestHandler<CreateMessageLogCommand, Result<MessageLogDto>>
{
    public async Task<Result<MessageLogDto>> Handle(
        CreateMessageLogCommand request, CancellationToken cancellationToken)
    {
        // Resolve required T2 PartnerPublicId (Guid AK) → T2 internal Id (long PK).
        var partner = await partnerRepository.GetByPublicIdAsync(
            request.PartnerPublicId, request.TenantId, cancellationToken);
        if (partner is null)
            return Result.Fail<MessageLogDto>("Partner not found");

        // Resolve optional T2 ContractPublicId (Guid AK) → T2 internal Id (long PK).
        // GetByPublicIdAsync scoped to TenantId — cross-tenant isolation via repo signature.
        long? contractInternalId = null;
        if (request.ContractPublicId.HasValue)
        {
            var contract = await contractRepository.GetByPublicIdAsync(
                request.ContractPublicId.Value, request.TenantId, cancellationToken);
            if (contract is null)
                return Result.Fail<MessageLogDto>("Contract not found");
            contractInternalId = contract.Id;
        }

        // T3 entity: factory takes IGuidGenerator to assign MessageId AK.
        var log = MessageLog.Create(
            guidGenerator, request.TenantId, partner.Id, contractInternalId,
            request.Direction, request.MessageType, request.Payload,
            request.CorrelationId, currentUser.UserId);

        await repository.AddAsync(log, cancellationToken);

        return Result.Ok(MapToDto(log, partner.PublicId, null));
    }

    // T3: Id slot = MessageId (Guid AK). PartnerId = Guid (Partner.PublicId AK). ContractId = Guid? (Contract.PublicId AK).
    // InternalId is 0 pre-SaveChanges; MessageId is set at Create time.
    // ContractPublicId not available post-Create (no round-trip); pass request value directly.
    private static MessageLogDto MapToDto(MessageLog m, Guid partnerPublicId, Guid? contractPublicId) => new(
        m.MessageId, m.TenantId, partnerPublicId, contractPublicId,
        m.Direction.ToString(), m.MessageType, m.Payload,
        m.Status.ToString(), m.CorrelationId,
        m.SentAt, m.AcknowledgedAt, m.CreatedAt, m.CreatedAt);
}
