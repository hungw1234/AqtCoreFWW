using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>Write-side repository for JitProviderConfig — read-side uses IReadDbConnection (Dapper).</summary>
public interface IJitProviderConfigRepository
{
    Task<JitProviderConfig?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<JitProviderConfig?> GetBySchemeAsync(string scheme, CancellationToken ct = default);
    Task AddAsync(JitProviderConfig entity, CancellationToken ct = default);
    Task UpdateAsync(JitProviderConfig entity, CancellationToken ct = default);
}
