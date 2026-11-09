using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Queries.GetChatHistory;

/// <summary>Returns paginated chat messages for a session (Dapper read path). SessionId = T1 long PK.</summary>
public sealed record GetChatHistoryQuery(
    long SessionId,
    Guid TenantId,
    int Page = 1,
    int PageSize = 50)
    : IRequest<Result<IReadOnlyList<ChatMessageDto>>>;

/// <summary>MessageId = T1 long PK of AiChatMessage.</summary>
public sealed record ChatMessageDto(
    // arch:suppress R_T2FK Message: regex collision — refers to AiChatMessage T1, not Collaboration.Message T2
    long MessageId,
    string Role,
    string? Content,
    string ContentHash,
    bool IsContentEncrypted,
    DateTimeOffset CreatedAt);
