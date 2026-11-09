using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

public sealed class DelegationRepository : IDelegationRepository
{
    private readonly IdentityDbContext _db;

    public DelegationRepository(IdentityDbContext db) => _db = db;

    public Task<UserDelegation?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default)
        => _db.UserDelegations.FirstOrDefaultAsync(d => d.PublicId == publicId, ct);

    public Task<bool> HasActiveOverlapAsync(
        Guid delegatorId, Guid delegateId,
        DateTime validFrom, DateTime validTo,
        CancellationToken ct = default)
        => _db.UserDelegations.AnyAsync(
            d => d.DelegatorId == delegatorId
              && d.DelegateId == delegateId
              && !d.IsRevoked
              && d.ValidFrom <= validTo
              && d.ValidTo >= validFrom, ct);

    public Task AddAsync(UserDelegation delegation, CancellationToken ct = default)
    {
        _db.UserDelegations.Add(delegation);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
