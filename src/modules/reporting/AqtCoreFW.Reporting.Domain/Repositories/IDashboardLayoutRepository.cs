using AqtCoreFW.Reporting.Domain.Entities;

namespace AqtCoreFW.Reporting.Domain.Repositories;

public interface IDashboardLayoutRepository
{
    /// <summary>T1: look up by internal long PK.</summary>
    Task<DashboardLayout?> GetByIdAsync(long id, CancellationToken ct = default);
    Task AddAsync(DashboardLayout layout, CancellationToken ct = default);
    Task<IReadOnlyList<DashboardLayout>> GetByOwnerAsync(Guid ownerId, Guid tenantId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
