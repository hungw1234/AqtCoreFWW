using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Commands.MarkAsRead;

public sealed class MarkAsReadCommandHandler(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IMessageReadStateRepository readStateRepository,
    IDomainEventPublisher eventPublisher,
    IGuidGenerator guidGenerator)
    : IRequestHandler<MarkAsReadCommand, Result>
{
    public async Task<Result> Handle(
        MarkAsReadCommand request,
        CancellationToken cancellationToken)
    {
        // request.ConversationId = PublicId (T2 Guid AK) — resolve to aggregate
        var conversation = await conversationRepository.GetByPublicIdAsync(
            request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Fail(new NotFoundError($"Conversation {request.ConversationId} not found."));

        // request.MessageId = PublicId (T2 Guid AK) — resolve to internal long for FK usage
        var message = await messageRepository.GetByPublicIdAsync(request.MessageId, cancellationToken);
        if (message is null)
            return Result.Fail(new NotFoundError($"Message {request.MessageId} not found."));

        // Only a participant may mark a conversation read. Previously a non-member silently no-opped
        // (member?.UpdateLastRead) and still got a success result — return 403 instead so a caller
        // outside the conversation is rejected, consistent with the send/delete membership gates.
        var member = conversation.Members.FirstOrDefault(m => m.UserId == request.UserId);
        if (member is null)
            return Result.Fail(new ForbiddenError(
                $"User {request.UserId} is not a participant of conversation {request.ConversationId}."));

        // Update the member's last-read pointer (the unread-count high-water mark) and flush it first, so
        // the pointer always persists independently of the per-message receipt write below.
        member.UpdateLastRead(message.Id);
        await conversationRepository.SaveChangesAsync(cancellationToken);

        // Record a per-message read-receipt (the "seen by" feature). Idempotent on the unique
        // (MessageId, UserId) index: only the FIRST read inserts a row and broadcasts the realtime event;
        // re-marking the same message is a no-op so receipts don't duplicate and "seen by" isn't spammed.
        var receipt = MessageReadState.Create(message.Id, request.UserId);
        var isFirstRead = await readStateRepository.AddIfNotExistsAsync(receipt, cancellationToken);
        if (isFirstRead)
        {
            await eventPublisher.PublishAsync(
                [new MessageReadEvent(
                    request.MessageId, conversation.PublicId, request.UserId,
                    conversation.TenantId, receipt.ReadAt, guidGenerator.NewIdRandom())],
                cancellationToken);
        }

        return Result.Ok();
    }
}
