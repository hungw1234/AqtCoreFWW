using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Notifications.Application.Commands.CreateNotification;

/// <summary>Command to create and queue a notification for delivery.</summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record CreateNotificationCommand(
    Guid TenantId,
    Guid RecipientUserId,
    string Subject,
    string Body,
    string Channel) : ICommand<Guid>;
