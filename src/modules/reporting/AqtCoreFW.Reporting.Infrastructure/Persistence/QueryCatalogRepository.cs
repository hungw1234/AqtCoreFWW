using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence;

public sealed class QueryCatalogRepository(ReportingDbContext dbContext)
    : IQueryCatalogRepository
{
    public async Task<QueryCatalogEntry?> GetByIdAsync(long id, CancellationToken ct = default) =>
        await dbContext.QueryCatalogEntries.FirstOrDefaultAsync(q => q.Id == id, ct);

    public async Task<QueryCatalogEntry?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        await dbContext.QueryCatalogEntries.FirstOrDefaultAsync(q => q.Code == code, ct);

    public async Task<IReadOnlyList<QueryCatalogEntry>> GetAllActiveAsync(CancellationToken ct = default) =>
        await dbContext.QueryCatalogEntries
            .Where(q => q.IsActive)
            .OrderBy(q => q.Code)
            .ToListAsync(ct);

    public async Task AddAsync(QueryCatalogEntry entry, CancellationToken ct = default)
    {
        await dbContext.QueryCatalogEntries.AddAsync(entry, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        dbContext.SaveChangesAsync(ct);
}
