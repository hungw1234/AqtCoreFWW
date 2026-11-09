using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence;

public sealed class DashboardLayoutRepository(ReportingDbContext dbContext)
    : IDashboardLayoutRepository
{
    public async Task<DashboardLayout?> GetByIdAsync(long id, CancellationToken ct = default) =>
        await dbContext.DashboardLayouts.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task AddAsync(DashboardLayout layout, CancellationToken ct = default)
    {
        await dbContext.DashboardLayouts.AddAsync(layout, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DashboardLayout>> GetByOwnerAsync(
        Guid ownerId,
        Guid tenantId,
        CancellationToken ct = default) =>
        await dbContext.DashboardLayouts
            .Where(d => d.OwnerId == ownerId && d.TenantId == tenantId)
            .OrderByDescending(d => d.IsDefault)
            .ThenBy(d => d.Name)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        dbContext.SaveChangesAsync(ct);
}
