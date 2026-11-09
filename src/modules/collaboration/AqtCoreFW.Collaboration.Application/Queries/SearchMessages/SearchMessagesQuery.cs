using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Collaboration.Application.Queries.SearchMessages;

public sealed record SearchMessagesQuery(
    Guid ConversationId,
    string SearchTerm,
    int PageSize = 20) : IQuery<IReadOnlyList<MessageDto>>;
