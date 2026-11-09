using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Notifications.Application.Commands.MarkNotificationRead;

/// <summary>Marks a single notification as read. Only the owning user may mark their own.</summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record MarkNotificationReadCommand(Guid NotificationId, Guid TenantId, Guid UserId) : ICommand;
