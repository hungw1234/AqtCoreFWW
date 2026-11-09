using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IMfaPolicyRepository.
/// Queries against [identity].MfaPolicies — uses global schema set by IdentityDbContext.
/// Soft-delete filter applied via EF global query filter (IsDeleted=false).
/// </summary>
public sealed class MfaPolicyRepository : IMfaPolicyRepository
{
    private readonly IdentityDbContext _db;

    public MfaPolicyRepository(IdentityDbContext db) => _db = db;

    public async Task<MfaPolicy?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default)
        => await _db.MfaPolicies.FirstOrDefaultAsync(p => p.PublicId == publicId, ct);

    public async Task<MfaPolicy?> GetByInternalIdAsync(long internalId, CancellationToken ct = default)
        => await _db.MfaPolicies.FirstOrDefaultAsync(p => p.Id == internalId, ct);

    public async Task<MfaPolicy?> GetByRoleAsync(string roleName, CancellationToken ct = default)
        => await _db.MfaPolicies
            .FirstOrDefaultAsync(p => p.Role == roleName, ct);

    public async Task<IReadOnlyList<MfaPolicy>> ListAllAsync(CancellationToken ct = default)
        => await _db.MfaPolicies.ToListAsync(ct);

    public async Task<IReadOnlyList<MfaPolicy>> GetByRoleNamesAsync(
        IEnumerable<string> roleNames, CancellationToken ct = default)
    {
        var names = roleNames.ToList();
        if (names.Count == 0)
            return Array.Empty<MfaPolicy>();

        return await _db.MfaPolicies
            .Where(p => names.Contains(p.Role))
            .ToListAsync(ct);
    }

    public async Task AddAsync(MfaPolicy policy, CancellationToken ct = default)
    {
        await _db.MfaPolicies.AddAsync(policy, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(MfaPolicy policy, CancellationToken ct = default)
    {
        _db.MfaPolicies.Update(policy);
        await _db.SaveChangesAsync(ct);
    }
}
