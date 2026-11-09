using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Collaboration.Application.Commands.AddMember;

// UserId = the member being added; RequestedBy = the caller (injected server-side from the JWT by
// the controller) used for the Owner-only authorization gate. Never bind RequestedBy from the body.
[AuditCategory(AuditCategory.AppAccess)]
public sealed record AddMemberCommand(
    Guid ConversationId,
    Guid UserId,
    MemberRole Role,
    Guid RequestedBy) : ICommand;
