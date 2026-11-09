using AqtCoreFW.BusinessProcess.Domain.Entities;

namespace AqtCoreFW.BusinessProcess.Domain.Repositories;

/// <summary>
/// Repository for BusinessProcessDefinition aggregate.
/// Reads include children navigations. Writes enforce Pattern A (R13) — pre-factory strict tenant guard.
/// </summary>
public interface IBusinessProcessRepository
{
    /// <summary>Load aggregate (with StateForms + TransitionForms) by external Guid PublicId.</summary>
    Task<BusinessProcessDefinition?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);

    /// <summary>Load aggregate (with children) by internal bigint Id.</summary>
    Task<BusinessProcessDefinition?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>List active BPs for caller's tenant (no children loaded — list view is shallow).</summary>
    Task<IReadOnlyList<BusinessProcessDefinition>> GetActiveByTenantAsync(CancellationToken ct = default);

    /// <summary>Look up by (tenant, Code) — unique per tenant. Used by activate / dedupe paths.</summary>
    Task<BusinessProcessDefinition?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Strict tenant guard. Throws InvalidOperationException with GOV_BP_002 on cross-tenant write.</summary>
    Task AddAsync(BusinessProcessDefinition bp, CancellationToken ct = default);

    /// <summary>Persist mutations on the tracked aggregate. RowVersion concurrency token enforced by EF.</summary>
    Task UpdateAsync(BusinessProcessDefinition bp, CancellationToken ct = default);
}
