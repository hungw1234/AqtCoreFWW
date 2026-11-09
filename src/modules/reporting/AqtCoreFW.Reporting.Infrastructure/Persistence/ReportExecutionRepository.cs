using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence;

public sealed class ReportExecutionRepository(ReportingDbContext dbContext)
    : IReportExecutionRepository
{
    public async Task<ReportExecution?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default) =>
        await dbContext.ReportExecutions.FirstOrDefaultAsync(r => r.PublicId == publicId, ct);

    public async Task<ReportExecution?> GetByInternalIdAsync(long id, CancellationToken ct = default) =>
        await dbContext.ReportExecutions.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddAsync(ReportExecution execution, CancellationToken ct = default)
    {
        await dbContext.ReportExecutions.AddAsync(execution, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReportExecution>> GetExpiredAsync(
        DateTimeOffset cutoff, CancellationToken ct = default) =>
        await dbContext.ReportExecutions
            .Where(r => r.CompletedAt != null && r.CompletedAt < cutoff)
            .ToListAsync(ct);

    public async Task<int> CountPendingAsync(string tenantId, CancellationToken ct = default) =>
        await dbContext.ReportExecutions
            .Where(r => r.RequestedBy.ToString() == tenantId &&
                        (r.Status == Domain.Enums.ExecutionStatus.Queued ||
                         r.Status == Domain.Enums.ExecutionStatus.Running))
            .CountAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        dbContext.SaveChangesAsync(ct);
}
