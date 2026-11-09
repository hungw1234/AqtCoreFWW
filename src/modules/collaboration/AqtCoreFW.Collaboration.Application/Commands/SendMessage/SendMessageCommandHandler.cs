using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Commands.SendMessage;

public sealed class SendMessageCommandHandler(
    IMessageRepository messageRepository,
    IConversationRepository conversationRepository,
    IDomainEventPublisher eventPublisher,
    IIdentityModuleClient identityClient,
    IGuidGenerator guidGen)
    : IRequestHandler<SendMessageCommand, Result<MessageDto>>
{
    public async Task<Result<MessageDto>> Handle(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        // request.ConversationId = PublicId (T2 Guid AK) — resolve to aggregate for internal Id
        var conversation = await conversationRepository.GetByPublicIdAsync(
            request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Fail(new NotFoundError($"Conversation {request.ConversationId} not found."));

        // Only participants may post — without this a caller could send into any conversation they can
        // name (the realtime broadcast then fans the message out to that conversation's group).
        if (!conversation.Members.Any(m => m.UserId == request.SenderId))
            return Result.Fail(new ForbiddenError(
                $"User {request.SenderId} is not a participant of conversation {request.ConversationId}."));

        // Resolve optional ReplyToId (Guid PublicId) to internal long FK
        long? replyToInternalId = null;
        if (request.ReplyToId.HasValue)
        {
            var replyToMsg = await messageRepository.GetByPublicIdAsync(request.ReplyToId.Value, cancellationToken);
            if (replyToMsg is null)
                return Result.Fail(new NotFoundError($"Reply-to message {request.ReplyToId.Value} not found."));
            replyToInternalId = replyToMsg.Id;
        }

        // conversation.Id = internal long PK (T2 FK for Message.ConversationId)
        var message = Message.Create(
            guidGen,
            conversation.Id,
            request.SenderId,
            request.Content,
            request.ContentType,
            request.TenantId,
            replyToInternalId);

        // conversation is tracked (loaded via TrackingQuery) and shares the same scoped DbContext as
        // messageRepository, so this mutation is flushed atomically by the AddAsync SaveChanges below —
        // one transaction commits both the new message and the LastMessageAt bump. Do NOT add a
        // separate SaveChanges here: it would split the write into two round-trips and lose atomicity.
        conversation.UpdateLastMessageAt(DateTime.UtcNow);
        await messageRepository.AddAsync(message, cancellationToken);

        await eventPublisher.PublishAsync(message.DomainEvents, cancellationToken);
        message.ClearDomainEvents();

        // Resolve sender name for real-time display (SignalR broadcast includes this DTO)
        var userMap = await identityClient.GetUserInfoByIdsAsync(
            [message.SenderId], request.TenantId, cancellationToken);
        var senderName = userMap.TryGetValue(message.SenderId, out var u) ? u.FullName : null;

        // Expose PublicId (Guid) as Id + ConversationPublicId (Guid) — never internal longs
        return Result.Ok(new MessageDto(
            message.PublicId,
            conversation.PublicId,
            message.SenderId,
            senderName,
            message.Content,
            message.ContentType.ToString(),
            request.ReplyToId,   // pass FE-facing Guid? back as-is (T2 PublicId)
            message.CreatedAt,
            message.EditedAt,
            message.IsMessageDeleted,
            message.IsSystemMessage));
    }
}
