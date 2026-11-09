using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

public sealed class AttributeRuleRepository : IAttributeRuleRepository
{
    private readonly IdentityDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AttributeRuleRepository> _logger;

    public AttributeRuleRepository(
        IdentityDbContext db,
        ITenantContext tenantContext,
        ILogger<AttributeRuleRepository> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // Tenant filter: current tenant's rules + system-wide rules (TenantId == null).
    // Enforced inline (not via HasQueryFilter) to prevent IgnoreQueryFilters bypass in Hangfire.
    public async Task<IReadOnlyList<AttributeRule>> GetByAttributeAsync(
        string attributeKey, string attributeValue,
        CancellationToken ct = default)
        => await _db.AttributeRules
            .AsNoTracking()
            .Where(r => r.AttributeKey == attributeKey && r.AttributeValue == attributeValue)
            .Where(r => r.TenantId == _tenantContext.TenantId || r.TenantId == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AttributeRule>> GetAllAsync(Guid? tenantId = null, CancellationToken ct = default)
        => await _db.AttributeRules
            .AsNoTracking()
            .Where(r => tenantId == null || r.TenantId == tenantId)
            .OrderBy(r => r.Resource).ThenBy(r => r.Action)
            .ToListAsync(ct);

    public async Task AddAsync(AttributeRule rule, CancellationToken ct = default)
    {
        // Defence-in-depth: reject cross-tenant ABAC rule create.
        // Global rules (TenantId == null) are allowed from any tenant context.
        if (rule.TenantId is not null && rule.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Cross-tenant ABAC rule create rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                _tenantContext.TenantId, rule.TenantId);
            throw new InvalidOperationException("Cross-tenant ABAC rule create rejected");
        }

        await _db.AttributeRules.AddAsync(rule, ct);
        await _db.SaveChangesAsync(ct);
    }

    // Tenant-scoped fetch. DeleteAsync delegates here to inherit the same isolation.
    public async Task<AttributeRule?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.AttributeRules
            .Where(r => r.Id == id)
            .Where(r => r.TenantId == _tenantContext.TenantId || r.TenantId == null)
            .FirstOrDefaultAsync(ct);

    public async Task UpdateAsync(AttributeRule rule, CancellationToken ct = default)
    {
        // Defence-in-depth: reject cross-tenant ABAC rule update.
        // Global rules (TenantId == null) are allowed from any tenant context.
        if (rule.TenantId is not null && rule.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Cross-tenant ABAC rule update rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                _tenantContext.TenantId, rule.TenantId);
            throw new InvalidOperationException("Cross-tenant ABAC rule update rejected");
        }

        _db.AttributeRules.Update(rule);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        // Route through GetByIdAsync so cross-tenant IDs are treated as not-found (no-op delete).
        var rule = await GetByIdAsync(id, ct);
        if (rule is not null)
        {
            _db.AttributeRules.Remove(rule);
            await _db.SaveChangesAsync(ct);
        }
    }
}
