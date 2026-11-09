using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Collaboration.Application.Commands.DeleteMessage;

// UserId = the caller (from JWT). The handler authorizes that the caller owns the message before
// deleting — without it any user in the tenant could delete anyone else's message (horizontal IDOR).
[AuditCategory(AuditCategory.AppAccess)]
public sealed record DeleteMessageCommand(Guid MessageId, Guid UserId) : ICommand;
