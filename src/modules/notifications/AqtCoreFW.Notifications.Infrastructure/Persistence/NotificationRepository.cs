using Microsoft.EntityFrameworkCore;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.Notifications.Domain.Repositories;

namespace AqtCoreFW.Notifications.Infrastructure.Persistence;

/// <summary>
/// EF Core write-side repository for Notification (T2 entity).
/// External addressing: PublicId (Guid AK). Internal FK: Id (long PK).
/// Read-side queries bypass this and use IReadDbConnection (Dapper).
/// </summary>
public sealed class NotificationRepository(NotificationsDbContext dbContext) : INotificationRepository
{
    public async Task<Notification?> GetByPublicIdAsync(
        Guid publicId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.PublicId == publicId && !n.IsDeleted, cancellationToken);
    }

    public async Task<Notification?> GetByInternalIdAsync(
        long id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && !n.IsDeleted, cancellationToken);
    }

    public async Task AddAsync(Notification entity, CancellationToken cancellationToken = default)
    {
        await dbContext.Notifications.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateAsync(Notification entity, CancellationToken cancellationToken = default)
    {
        dbContext.Notifications.Update(entity);
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateRangeAsync(IEnumerable<Notification> entities, CancellationToken cancellationToken = default)
    {
        dbContext.Notifications.UpdateRange(entities);
        return dbContext.SaveChangesAsync(cancellationToken); // single round-trip for the whole batch
    }

    public async Task DeleteAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.PublicId == publicId, cancellationToken);
        if (entity is null) return;
        entity.Delete(); // soft delete via AuditableEntity base
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> GetUnreadByUserAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Notifications
            .Where(n => n.RecipientUserId == userId
                     && n.TenantId == tenantId
                     && !n.IsRead
                     && !n.IsDeleted)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
