using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.Notifications.Application.DTOs;

namespace AqtCoreFW.Notifications.Application.Queries.GetNotificationTemplateById;

/// <summary>Returns a single notification template by Guid PublicId (T2 entity external AK).</summary>
public sealed record GetNotificationTemplateByIdQuery(Guid Id) : IQuery<NotificationTemplateDto>;
