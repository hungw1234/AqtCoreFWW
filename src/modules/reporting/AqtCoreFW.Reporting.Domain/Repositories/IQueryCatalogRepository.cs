using AqtCoreFW.Reporting.Domain.Entities;

namespace AqtCoreFW.Reporting.Domain.Repositories;

public interface IQueryCatalogRepository
{
    /// <summary>T1: look up by internal long PK.</summary>
    Task<QueryCatalogEntry?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<QueryCatalogEntry?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<QueryCatalogEntry>> GetAllActiveAsync(CancellationToken ct = default);
    Task AddAsync(QueryCatalogEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
