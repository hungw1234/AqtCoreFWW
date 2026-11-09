using AqtCoreFW.Notifications.Domain.Entities;

namespace AqtCoreFW.Notifications.Domain.Repositories;

/// <summary>
/// Write-side repository for the NotificationTemplate aggregate (T2 entity — long Id PK + Guid PublicId AK).
/// Read-side handlers use IReadDbConnection (Dapper) directly.
/// </summary>
public interface INotificationTemplateRepository
{
    /// <summary>Looks up by Guid PublicId (external AK) with cross-tenant guard.</summary>
    Task<NotificationTemplate?> GetByPublicIdAsync(Guid publicId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Looks up by long internal PK — BE-internal use only (e.g. dedup resolution).</summary>
    Task<NotificationTemplate?> GetByInternalIdAsync(long id, Guid tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(NotificationTemplate entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(NotificationTemplate entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task<NotificationTemplate?> FindByKeyAsync(
        string templateKey, string channel, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Resolves PublicId → internal long Id. Returns null if not found or cross-tenant.</summary>
    Task<long?> GetInternalIdByPublicIdAsync(Guid publicId, Guid tenantId, CancellationToken cancellationToken = default);
}
