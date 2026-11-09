using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Workflow.Application.Commands.DeleteNotificationConfig;

/// <summary>Soft-deletes a notification config by ID.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteNotificationConfigCommand(
    long ConfigId,
    Guid TenantId) : ICommand;
