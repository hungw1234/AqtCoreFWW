using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Enums;

namespace AqtCoreFW.Reporting.Domain.Repositories;

public interface IReportExecutionRepository
{
    /// <summary>T2: look up by PublicId (Guid AK) — used by Application/Jobs layer.</summary>
    Task<ReportExecution?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);
    /// <summary>T2: look up by internal PK (long) — used by Infrastructure resolving FK.</summary>
    Task<ReportExecution?> GetByInternalIdAsync(long id, CancellationToken ct = default);
    Task AddAsync(ReportExecution execution, CancellationToken ct = default);
    Task<IReadOnlyList<ReportExecution>> GetExpiredAsync(DateTimeOffset cutoff, CancellationToken ct = default);
    /// <summary>Count pending/running executions for a tenant (rate limiting).</summary>
    Task<int> CountPendingAsync(string tenantId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
