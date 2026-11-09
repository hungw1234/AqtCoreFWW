using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>EF Core implementation of IAccessReviewRepository.</summary>
public sealed class AccessReviewRepository : IAccessReviewRepository
{
    private readonly IdentityDbContext _db;

    public AccessReviewRepository(IdentityDbContext db) => _db = db;

    public async Task<AccessReviewRecord?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default)
        => await _db.AccessReviewRecords.FirstOrDefaultAsync(r => r.PublicId == publicId, ct);

    public async Task<IReadOnlyList<AccessReviewRecord>> ListPendingAsync(
        Guid? tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        // AccessReviewRecord has no TenantId — filtered by linked user's tenant if needed
        // For now: return all pending records ordered by due date (teams extend as needed)
        return await _db.AccessReviewRecords
            .AsNoTracking()
            .Where(r => r.Decision == null)
            .OrderBy(r => r.NextReviewDue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(AccessReviewRecord record, CancellationToken ct = default)
    {
        _db.AccessReviewRecords.Update(record);
        await _db.SaveChangesAsync(ct);
    }
}
