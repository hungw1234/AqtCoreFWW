using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Collaboration.Application.Queries.GetConversations;

public sealed record GetConversationsQuery(
    Guid UserId,
    Guid TenantId,
    int Page = 1,
    int PageSize = 20) : IQuery<IReadOnlyList<ConversationDto>>;
