using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Collaboration.Application.Queries.GetMessages;

public sealed record GetMessagesQuery(
    Guid ConversationId,
    int PageSize = 50,
    Guid? BeforeMessageId = null,
    Guid? AfterMessageId = null) : IQuery<IReadOnlyList<MessageDto>>;
