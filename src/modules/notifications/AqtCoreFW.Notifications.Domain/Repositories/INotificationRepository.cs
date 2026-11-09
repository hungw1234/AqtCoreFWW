using AqtCoreFW.Notifications.Domain.Entities;

namespace AqtCoreFW.Notifications.Domain.Repositories;

/// <summary>
/// Write-side repository for the Notification aggregate (T2 entity).
/// External addressing via PublicId (Guid AK); internal FK lookup via Id (long PK).
/// Read-side handlers use IReadDbConnection (Dapper) directly.
/// </summary>
public interface INotificationRepository
{
    /// <summary>Gets Notification by external Guid PublicId. Null if not found.</summary>
    Task<Notification?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>Gets Notification by internal long PK (for FK lookups). Null if not found.</summary>
    Task<Notification?> GetByInternalIdAsync(long id, CancellationToken cancellationToken = default);

    Task AddAsync(Notification entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(Notification entity, CancellationToken cancellationToken = default);

    /// <summary>Persists a batch of mutated notifications in a SINGLE SaveChanges round-trip
    /// (e.g. mark-all-read) — avoids the N round-trips of calling UpdateAsync per entity.</summary>
    Task UpdateRangeAsync(IEnumerable<Notification> entities, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid publicId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> GetUnreadByUserAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}
