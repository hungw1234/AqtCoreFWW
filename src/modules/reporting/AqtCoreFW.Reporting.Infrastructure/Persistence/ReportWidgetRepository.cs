using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence;

public sealed class ReportWidgetRepository(ReportingDbContext dbContext)
    : IReportWidgetRepository
{
    public async Task<ReportWidget?> GetByIdAsync(long id, CancellationToken ct = default) =>
        await dbContext.ReportWidgets.FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task AddAsync(ReportWidget widget, CancellationToken ct = default)
    {
        await dbContext.ReportWidgets.AddAsync(widget, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReportWidget>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await dbContext.ReportWidgets
            .Where(w => w.TenantId == tenantId)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        dbContext.SaveChangesAsync(ct);
}
