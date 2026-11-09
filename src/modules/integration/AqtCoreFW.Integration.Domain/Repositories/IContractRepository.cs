using AqtCoreFW.Integration.Domain.Entities;

namespace AqtCoreFW.Integration.Domain.Repositories;

public interface IContractRepository
{
    /// <summary>Look up by T2 PublicId AK (Guid) scoped to tenant — external-facing (API routes, command handlers).</summary>
    Task<Contract?> GetByPublicIdAsync(Guid publicId, Guid tenantId, CancellationToken ct);
    /// <summary>Look up by T2 internal PK (long) — internal use only.</summary>
    Task<Contract?> GetByInternalIdAsync(long id, CancellationToken ct);
    Task AddAsync(Contract contract, CancellationToken ct);
    Task UpdateAsync(Contract contract, CancellationToken ct);
}
