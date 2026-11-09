using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Collaboration.Application.Commands.ArchiveConversation;

// RequestedBy = the caller (injected server-side from the JWT by the controller), used for the
// Owner-only authorization gate. Never bind it from the request body.
[AuditCategory(AuditCategory.AppAccess)]
public sealed record ArchiveConversationCommand(Guid ConversationId, Guid RequestedBy) : ICommand;
