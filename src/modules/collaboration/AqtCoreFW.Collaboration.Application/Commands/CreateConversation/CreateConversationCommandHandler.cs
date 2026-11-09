using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Commands.CreateConversation;

public sealed class CreateConversationCommandHandler(
    IConversationRepository repository,
    IDomainEventPublisher eventPublisher,
    IGuidGenerator guidGen)
    : IRequestHandler<CreateConversationCommand, Result<ConversationDto>>
{
    public async Task<Result<ConversationDto>> Handle(
        CreateConversationCommand request,
        CancellationToken cancellationToken)
    {
        // Duplicate Direct guard: return existing conversation if pair already exists
        if (request.Type == ConversationType.Direct && request.MemberUserIds.Count == 1)
        {
            var existing = await repository.FindDirectBetweenAsync(
                request.CreatedBy, request.MemberUserIds[0], request.TenantId, cancellationToken);
            if (existing is not null)
                return Result.Ok(new ConversationDto(
                    existing.PublicId,  // T2 AK: expose PublicId as Id (Guid) — never long
                    existing.Type.ToString(), existing.Name,
                    existing.CreatedBy, existing.LastMessageAt, existing.IsArchived,
                    existing.Members.Count));
        }

        // T2 factory: IGuidGenerator injects monotonic Guid for PublicId AK
        var conversation = Conversation.Create(
            guidGen,
            request.Type,
            request.Name,
            request.CreatedBy,
            request.TenantId);

        // ConversationMember.Create takes long conversationId — must use conversation.Id (internal PK).
        // Id is 0 pre-SaveChanges (EF identity column); members are cascade-saved with the aggregate.
        // This is a T1 pre-SaveChanges pattern: ConversationMember FK resolved after SaveChanges in DB.
        var creator = ConversationMember.Create(conversation.Id, request.CreatedBy, MemberRole.Owner);
        conversation.AddMember(creator);
        foreach (var userId in request.MemberUserIds.Where(id => id != request.CreatedBy))
        {
            conversation.AddMember(ConversationMember.Create(conversation.Id, userId, MemberRole.Member));
        }

        await repository.AddAsync(conversation, cancellationToken);

        await eventPublisher.PublishAsync(conversation.DomainEvents, cancellationToken);
        conversation.ClearDomainEvents();

        // Expose PublicId (Guid) as Id — FE never sees internal long Id
        return Result.Ok(new ConversationDto(
            conversation.PublicId,
            conversation.Type.ToString(),
            conversation.Name,
            conversation.CreatedBy,
            conversation.LastMessageAt,
            conversation.IsArchived,
            request.MemberUserIds.Count));
    }
}
