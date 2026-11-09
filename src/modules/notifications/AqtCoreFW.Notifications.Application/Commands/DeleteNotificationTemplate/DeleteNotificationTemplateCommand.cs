using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Notifications.Application.Commands.DeleteNotificationTemplate;

/// <summary>Soft-deletes a notification template by Guid PublicId (T2 entity external AK).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteNotificationTemplateCommand(Guid Id) : ICommand;
