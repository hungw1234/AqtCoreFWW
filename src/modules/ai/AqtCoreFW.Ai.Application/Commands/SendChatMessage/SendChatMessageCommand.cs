using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Commands.SendChatMessage;

/// <summary>
/// Sends a user message to an AI session — routes via IAiRoutingService.
/// Creates a new session if SessionId is null.
/// Persists both user message and assistant response to SQL Server (GOV audit).
/// SessionId is T1 long PK (AiSession.Id).
/// </summary>
public sealed record SendChatMessageCommand(
    long? SessionId,
    string Message,
    Guid TenantId,
    Guid UserId,
    ClassificationLevel Classification = ClassificationLevel.Internal)
    : IRequest<Result<SendChatMessageResponse>>;

/// <summary>SessionId and MessageId are T1 long PKs (AiSession + AiChatMessage).</summary>
public sealed record SendChatMessageResponse(
    long SessionId,
    // arch:suppress R_T2FK Message: regex collision — refers to AiChatMessage T1, not Collaboration.Message T2
    long MessageId,
    string Response);
