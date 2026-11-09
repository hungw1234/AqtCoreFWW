using AqtCoreFW.Integration.Domain.Entities;

namespace AqtCoreFW.Integration.Domain.Repositories;

public interface IPartnerRepository
{
    /// <summary>Look up by T2 public Guid AK (external addressing — controller routes, command handlers).</summary>
    Task<Partner?> GetByPublicIdAsync(Guid publicId, Guid tenantId, CancellationToken ct = default);

    Task AddAsync(Partner partner, CancellationToken ct);
    Task UpdateAsync(Partner partner, CancellationToken ct);
}
