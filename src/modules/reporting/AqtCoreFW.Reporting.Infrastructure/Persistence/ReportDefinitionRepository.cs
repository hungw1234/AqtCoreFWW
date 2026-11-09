using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence;

public sealed class ReportDefinitionRepository(ReportingDbContext dbContext)
    : IReportDefinitionRepository
{
    public async Task<ReportDefinition?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default) =>
        await dbContext.ReportDefinitions.FirstOrDefaultAsync(r => r.PublicId == publicId, ct);

    public async Task<ReportDefinition?> GetByInternalIdAsync(long id, CancellationToken ct = default) =>
        await dbContext.ReportDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<ReportDefinition>> GetAllAsync(string? tenantId, CancellationToken ct = default) =>
        await dbContext.ReportDefinitions
            .Where(r => r.TenantId == null || r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public async Task AddAsync(ReportDefinition definition, CancellationToken ct = default)
    {
        await dbContext.ReportDefinitions.AddAsync(definition, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        dbContext.SaveChangesAsync(ct);
}
