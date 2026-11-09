using AqtCoreFW.Notifications.Domain.Entities;

namespace AqtCoreFW.Notifications.Domain.Repositories;

/// <summary>
/// Idempotency log repository — guards against double-send on retries.
/// TemplateId is long (FK to T1 NotificationTemplate.Id internal PK).
/// </summary>
public interface INotificationLogRepository
{
    Task<bool> ExistsAsync(long templateId, Guid recipientId, string correlationId, CancellationToken cancellationToken = default);
    Task AddAsync(NotificationLog log, CancellationToken cancellationToken = default);
}
