using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.Ai.Domain.Errors;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Commands.SendChatMessage;

/// <summary>
/// Handles chat message — routes prompt, persists session/messages.
/// Confidential+ messages: stores ContentHash only (not raw content) per audit tier policy.
/// </summary>
public sealed class SendChatMessageCommandHandler(
    IAiRoutingService routingService,
    IAiSessionRepository sessionRepository,
    IGuidGenerator guidGenerator)
    : IRequestHandler<SendChatMessageCommand, Result<SendChatMessageResponse>>
{
    public async Task<Result<SendChatMessageResponse>> Handle(
        SendChatMessageCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve or create session (T1 long Id)
        AiSession session;
        if (request.SessionId.HasValue)
        {
            session = await sessionRepository.GetByIdAsync(request.SessionId.Value, cancellationToken)
                      ?? AiSession.Create(request.TenantId, request.UserId);
        }
        else
        {
            session = AiSession.Create(request.TenantId, request.UserId);
            await sessionRepository.AddAsync(session, cancellationToken);
        }

        // Persist user message (audit tier: hash-only for Confidential+)
        var isConfidential = request.Classification >= ClassificationLevel.Confidential;
        if (isConfidential)
            session.AddConfidentialMessage(ChatRole.User, request.Message);
        else
            session.AddMessage(ChatRole.User, request.Message);

        // Route through data sovereignty layer — correlation ID via IGuidGenerator (not Guid.NewGuid)
        var ctx = new AiContext(request.TenantId, request.UserId,
            guidGenerator.NewIdRandom().ToString(), request.Classification);

        var routeResult = await routingService.RouteAsync(
            request.Message, request.Classification, ctx, cancellationToken);

        if (routeResult.IsFailed)
            return Result.Fail<SendChatMessageResponse>(routeResult.Errors);

        var aiResponse = routeResult.Value;

        // Persist assistant response
        AiChatMessage assistantMsg;
        if (isConfidential)
            assistantMsg = session.AddConfidentialMessage(ChatRole.Assistant, aiResponse);
        else
            assistantMsg = session.AddMessage(ChatRole.Assistant, aiResponse);

        await sessionRepository.SaveChangesAsync(cancellationToken);

        // Return T1 long Ids (AiSession.Id + AiChatMessage.Id)
        return Result.Ok(new SendChatMessageResponse(session.Id, assistantMsg.Id, aiResponse));
    }
}
