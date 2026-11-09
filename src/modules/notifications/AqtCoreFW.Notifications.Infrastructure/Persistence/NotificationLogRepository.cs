using Microsoft.EntityFrameworkCore;
using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.Notifications.Domain.Repositories;

namespace AqtCoreFW.Notifications.Infrastructure.Persistence;

/// <summary>
/// EF Core repository for NotificationLog (T3 plain-class — append-only idempotency guard).
/// TemplateId is long (FK to T1 NotificationTemplate.Id internal PK).
/// </summary>
public sealed class NotificationLogRepository(NotificationsDbContext dbContext) : INotificationLogRepository
{
    public async Task<bool> ExistsAsync(
        long templateId, Guid recipientId, string correlationId, CancellationToken cancellationToken = default)
    {
        return await dbContext.NotificationLogs
            .AnyAsync(l =>
                l.TemplateId == templateId &&
                l.RecipientId == recipientId &&
                l.CorrelationId == correlationId,
                cancellationToken);
    }

    public async Task AddAsync(NotificationLog log, CancellationToken cancellationToken = default)
    {
        await dbContext.NotificationLogs.AddAsync(log, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
