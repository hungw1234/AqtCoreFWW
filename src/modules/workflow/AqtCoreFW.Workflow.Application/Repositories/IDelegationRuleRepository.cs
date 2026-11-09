using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Write-side repository for DelegationRule aggregate.
/// GetActiveRuleAsync: used by CreateWorkflowTaskCommandHandler to auto-delegate on creation.
/// HasOverlappingRuleAsync: used by Create/UpdateDelegationRule validators to enforce max-1-active-rule.
/// Phase-4b B4c: T1 entity — IRepository&lt;T, long&gt; key is internal bigint PK.
/// </summary>
public interface IDelegationRuleRepository : IRepository<DelegationRule, long>
{
    /// <summary>
    /// Returns the single active (IsActive=true, window covers DateTimeOffset.UtcNow) rule
    /// for the given delegator within the tenant, or null if none exists.
    /// </summary>
    Task<DelegationRule?> GetActiveRuleAsync(
        Guid delegatorUserId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a rule for the given userId/tenantId overlaps the [validFrom, validUntil]
    /// window and IsActive=true. Pass excludeId (long) to ignore the rule being updated.
    /// Used to enforce max-1-active-rule constraint.
    /// </summary>
    Task<bool> HasOverlappingRuleAsync(
        Guid delegatorUserId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        Guid tenantId,
        long? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tenant-scoped lookup: returns the rule only if it belongs to the given tenant.
    /// Uses FirstOrDefaultAsync (respects global query filters) + explicit TenantId check.
    /// Returns NotFoundError if the rule does not exist or belongs to a different tenant.
    /// </summary>
    Task<Result<DelegationRule>> GetByIdAndTenantAsync(
        long id,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
