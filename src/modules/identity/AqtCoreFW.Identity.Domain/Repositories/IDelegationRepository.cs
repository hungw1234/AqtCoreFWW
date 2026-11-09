using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>Write-side delegation repository — read-side uses IReadDbConnection (Dapper).</summary>
public interface IDelegationRepository
{
    /// <summary>Gets delegation by PublicId (Guid AK — T2 entity).</summary>
    Task<UserDelegation?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);
    Task<bool> HasActiveOverlapAsync(Guid delegatorId, Guid delegateId, DateTime validFrom, DateTime validTo, CancellationToken ct = default);
    Task AddAsync(UserDelegation delegation, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
