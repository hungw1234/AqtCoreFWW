using AqtCoreFW.Reporting.Domain.Entities;

namespace AqtCoreFW.Reporting.Domain.Repositories;

public interface IReportDefinitionRepository
{
    /// <summary>T2: look up by PublicId (Guid AK) — used by Application/Presentation layer.</summary>
    Task<ReportDefinition?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);
    /// <summary>T2: look up by internal PK (long) — used by Infrastructure/Jobs resolving FK.</summary>
    Task<ReportDefinition?> GetByInternalIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<ReportDefinition>> GetAllAsync(string? tenantId, CancellationToken ct = default);
    Task AddAsync(ReportDefinition definition, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
