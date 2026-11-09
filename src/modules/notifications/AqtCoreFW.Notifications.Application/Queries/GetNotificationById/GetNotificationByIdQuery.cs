using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.Notifications.Application.DTOs;

namespace AqtCoreFW.Notifications.Application.Queries.GetNotificationById;

public sealed record GetNotificationByIdQuery(Guid Id, Guid TenantId) : IQuery<NotificationDto>;
