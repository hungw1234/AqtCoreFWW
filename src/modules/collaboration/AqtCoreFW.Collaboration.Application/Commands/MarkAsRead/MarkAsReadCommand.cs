using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Collaboration.Application.Commands.MarkAsRead;

[AuditCategory(AuditCategory.AppAccess)]
public sealed record MarkAsReadCommand(
    Guid ConversationId,
    Guid MessageId,
    Guid UserId) : ICommand;
