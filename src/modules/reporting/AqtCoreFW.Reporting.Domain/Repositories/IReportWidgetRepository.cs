using AqtCoreFW.Reporting.Domain.Entities;

namespace AqtCoreFW.Reporting.Domain.Repositories;

public interface IReportWidgetRepository
{
    /// <summary>T1: look up by internal long PK.</summary>
    Task<ReportWidget?> GetByIdAsync(long id, CancellationToken ct = default);
    Task AddAsync(ReportWidget widget, CancellationToken ct = default);
    Task<IReadOnlyList<ReportWidget>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
