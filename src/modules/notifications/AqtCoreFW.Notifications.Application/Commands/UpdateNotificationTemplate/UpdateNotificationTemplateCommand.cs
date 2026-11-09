using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Notifications.Application.Commands.UpdateNotificationTemplate;

/// <summary>Updates SubjectTemplate and BodyTemplate of an existing notification template.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateNotificationTemplateCommand(
    Guid Id, // T2: Guid PublicId (external AK)
    string SubjectTemplate,
    string BodyTemplate) : ICommand;
