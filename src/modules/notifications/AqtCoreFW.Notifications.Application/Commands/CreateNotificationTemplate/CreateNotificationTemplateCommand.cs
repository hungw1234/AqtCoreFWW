using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Notifications.Application.Commands.CreateNotificationTemplate;

/// <summary>Creates a new notification template under the given tenant.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateNotificationTemplateCommand(
    Guid TenantId,
    string TemplateKey,
    string SubjectTemplate,
    string BodyTemplate,
    string Channel) : ICommand<Guid>; // T2: returns PublicId (Guid AK)
