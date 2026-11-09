using Microsoft.EntityFrameworkCore;
using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.Notifications.Domain.Repositories;

namespace AqtCoreFW.Notifications.Infrastructure.Persistence;

/// <summary>
/// EF Core write-side repository for NotificationTemplate (T2 entity — long Id PK + Guid PublicId AK).
/// Read-side queries bypass this and use IReadDbConnection (Dapper).
/// </summary>
public sealed class NotificationTemplateRepository(NotificationsDbContext dbContext)
    : INotificationTemplateRepository
{
    public async Task<NotificationTemplate?> GetByPublicIdAsync(
        Guid publicId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await dbContext.NotificationTemplates
            .FirstOrDefaultAsync(t => t.PublicId == publicId && t.TenantId == tenantId && !t.IsDeleted, cancellationToken);
    }

    public async Task<NotificationTemplate?> GetByInternalIdAsync(
        long id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await dbContext.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId && !t.IsDeleted, cancellationToken);
    }

    public async Task<long?> GetInternalIdByPublicIdAsync(
        Guid publicId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.NotificationTemplates
            .Where(t => t.PublicId == publicId && t.TenantId == tenantId && !t.IsDeleted)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return entity;
    }

    public async Task AddAsync(NotificationTemplate entity, CancellationToken cancellationToken = default)
    {
        await dbContext.NotificationTemplates.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateAsync(NotificationTemplate entity, CancellationToken cancellationToken = default)
    {
        dbContext.NotificationTemplates.Update(entity);
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null) return;
        entity.Delete(); // soft delete via AuditableEntity base
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<NotificationTemplate?> FindByKeyAsync(
        string templateKey, string channel, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // EF Core cannot translate Channel.Value or .ToLowerInvariant() inline.
        // Pre-compute the comparison value and use the converted column directly.
        var channelLower = channel.ToLowerInvariant();
        return await dbContext.NotificationTemplates
            .FirstOrDefaultAsync(t =>
                t.TemplateKey == templateKey &&
                t.Channel == Domain.ValueObjects.NotificationChannel.From(channelLower) &&
                t.TenantId == tenantId &&
                !t.IsDeleted,
                cancellationToken);
    }
}
