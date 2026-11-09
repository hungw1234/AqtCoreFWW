using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Collaboration.Application.Commands.SendMessage;

[AuditCategory(AuditCategory.AppAccess)]
public sealed record SendMessageCommand(
    Guid ConversationId,
    Guid SenderId,
    string Content,
    ContentType ContentType,
    Guid TenantId,
    Guid? ReplyToId = null) : ICommand<MessageDto>;
