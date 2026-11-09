using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>Write-side repository for ExternalIdentity — read-side uses IReadDbConnection (Dapper).</summary>
public interface IExternalIdentityRepository
{
    Task<ExternalIdentity?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<ExternalIdentity?> GetByUserAndProviderAsync(Guid userId, ExternalIdentityProvider provider, CancellationToken ct = default);
    Task<ExternalIdentity?> GetByProviderAndExternalIdAsync(ExternalIdentityProvider provider, string externalId, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalIdentity>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(ExternalIdentity entity, CancellationToken ct = default);
    Task UpdateAsync(ExternalIdentity entity, CancellationToken ct = default);
    Task DeleteAsync(ExternalIdentity entity, CancellationToken ct = default);
}
