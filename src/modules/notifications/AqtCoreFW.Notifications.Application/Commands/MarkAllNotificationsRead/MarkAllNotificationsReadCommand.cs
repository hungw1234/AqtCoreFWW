using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Notifications.Application.Commands.MarkAllNotificationsRead;

/// <summary>Marks all unread notifications as read for a given user + tenant.</summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record MarkAllNotificationsReadCommand(Guid UserId, Guid TenantId) : ICommand;
