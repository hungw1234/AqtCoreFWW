using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Notifications.Application.Commands.SendNotification;

/// <summary>
/// Unified command to send a notification via any channel.
/// Performs dedup check via NotificationLog before dispatch.
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record SendNotificationCommand(
    Guid TenantId,
    Guid RecipientUserId,
    string Subject,
    string Body,
    string Channel,
    /// <summary>
    /// T2 external Guid PublicId of the NotificationTemplate.
    /// Handler resolves PublicId → internal long Id via GetInternalIdByPublicIdAsync before storing FK.
    /// </summary>
    Guid? TemplateId = null,
    string? CorrelationId = null) : ICommand<Guid>;
