using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core repository for Tenant entity — write-side.
/// Read-side queries use IReadDbConnection (Dapper) for enriched DTOs.
/// </summary>
public sealed class TenantRepository(IdentityDbContext db) : ITenantRepository
{
    public async Task<Tenant?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default)
        => await db.Tenants.FirstOrDefaultAsync(t => t.PublicId == publicId, ct);

    public async Task<Tenant?> GetByInternalIdAsync(long id, CancellationToken ct = default)
        => await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Tenant?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await db.Tenants.FirstOrDefaultAsync(
            t => t.Code.ToUpper() == code.ToUpper(), ct);

    public async Task<bool> ExistsAsync(Guid publicId, CancellationToken ct = default)
        => await db.Tenants.AnyAsync(t => t.PublicId == publicId, ct);

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default)
        => await db.Tenants.AnyAsync(
            t => t.Code.ToUpper() == code.ToUpper(), ct);

    public async Task<(IReadOnlyList<Tenant> Items, int TotalCount)> ListAsync(
        string? search,
        TenantStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = db.Tenants.AsQueryable();

        // Filter by status (default: exclude Archived unless explicitly requested)
        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }
        else
        {
            // Default: exclude Archived
            query = query.Where(t => t.Status != TenantStatus.Archived);
        }

        // Search by Name or Code
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim().ToUpper();
            query = query.Where(t =>
                t.Name.ToUpper().Contains(searchTerm) ||
                t.Code.ToUpper().Contains(searchTerm));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task AddAsync(Tenant tenant, CancellationToken ct = default)
    {
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);
}
