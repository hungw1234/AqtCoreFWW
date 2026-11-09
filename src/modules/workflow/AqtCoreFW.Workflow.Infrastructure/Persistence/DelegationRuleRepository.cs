using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// EF Core repository for DelegationRule aggregate.
/// T1 entity: IRepository key is long (bigint PK). GenericRepository&lt;T, long&gt;.
/// Phase-4b B4c: changed from GenericRepository&lt;T, Guid&gt; to long.
///   HasOverlappingRuleAsync: excludeId changed Guid?→long?.
///   GetByIdAndTenantAsync: id changed Guid→long.
///   DelegatorUserId/DelegateUserId remain Guid (FK exception — user refs).
/// </summary>
public sealed class DelegationRuleRepository(WorkflowDbContext context)
    : GenericRepository<DelegationRule, long>(context), IDelegationRuleRepository
{
    private readonly WorkflowDbContext _context = context;

    /// <inheritdoc/>
    public async Task<DelegationRule?> GetActiveRuleAsync(
        Guid delegatorUserId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await _context.DelegationRules
            .Where(r => r.DelegatorUserId == delegatorUserId
                     && r.TenantId == tenantId
                     && r.IsActive
                     && r.ValidFrom <= now
                     && r.ValidUntil >= now)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> HasOverlappingRuleAsync(
        Guid delegatorUserId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        Guid tenantId,
        long? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DelegationRules
            .Where(r => r.DelegatorUserId == delegatorUserId
                     && r.TenantId == tenantId
                     && r.IsActive
                     // Overlap: existing window intersects the new window
                     && r.ValidFrom < validUntil
                     && r.ValidUntil > validFrom);

        if (excludeId.HasValue)
            query = query.Where(r => r.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<DelegationRule>> GetByIdAndTenantAsync(
        long id,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // FirstOrDefaultAsync respects global query filters (soft-delete, tenant).
        // Explicit TenantId predicate prevents cross-tenant access even if filters are bypassed.
        var rule = await _context.DelegationRules
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, cancellationToken);

        return rule is null
            ? Result.Fail(new NotFoundError($"DelegationRule '{id}' not found."))
            : Result.Ok(rule);
    }
}
