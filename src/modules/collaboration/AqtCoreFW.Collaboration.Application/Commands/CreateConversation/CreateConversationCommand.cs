using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Collaboration.Application.Commands.CreateConversation;

[AuditCategory(AuditCategory.AppAccess)]
public sealed record CreateConversationCommand(
    ConversationType Type,
    string? Name,
    IReadOnlyList<Guid> MemberUserIds,
    Guid CreatedBy,
    Guid TenantId) : ICommand<ConversationDto>;
