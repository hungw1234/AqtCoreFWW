using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Infrastructure.Repositories;

/// <summary>
/// EF Core write-side repository for the RuleSet aggregate.
/// Loads Rules + Versions navigation — query handlers bypass this and use IReadDbConnection.
/// Defence-in-depth: inline tenant filter on reads + strict guards on writes.
/// T2 entity: external addressing via PublicId (Guid AK), internal FK via Id (long PK).
/// </summary>
public sealed class RuleSetRepository : IRuleSetRepository
{
    private readonly RulesDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RuleSetRepository> _logger;

    public RuleSetRepository(
        RulesDbContext db,
        ITenantContext tenantContext,
        ILogger<RuleSetRepository> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Returns RuleSet (Rules + Versions loaded) by external Guid PublicId.
    /// Inline tenant filter: SystemAdmin bypasses; regular users see own tenant only.
    /// </summary>
    public async Task<RuleSet?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default)
        => await _db.RuleSets
            .Include(rs => rs.Rules)
            .Include(rs => rs.Versions)
            .Where(rs => _tenantContext.IsSystemAdmin || rs.TenantId == _tenantContext.TenantId)
            .FirstOrDefaultAsync(rs => rs.PublicId == publicId, ct);

    /// <summary>
    /// Returns RuleSet (Rules + Versions loaded) by internal long Id (FK lookup path).
    /// Same inline tenant filter as GetByPublicIdAsync — used when handlers hold an internal FK
    /// (e.g., RunTestCase has testCase.RuleSetId as long).
    /// </summary>
    public async Task<RuleSet?> GetByInternalIdAsync(long internalId, CancellationToken ct = default)
        => await _db.RuleSets
            .Include(rs => rs.Rules)
            .Include(rs => rs.Versions)
            .Where(rs => _tenantContext.IsSystemAdmin || rs.TenantId == _tenantContext.TenantId)
            .FirstOrDefaultAsync(rs => rs.Id == internalId, ct);

    /// <summary>
    /// Strict tenant guard — no SystemAdmin bypass on writes to prevent mis-tagging.
    /// </summary>
    public async Task AddAsync(RuleSet ruleSet, CancellationToken ct = default)
    {
        if (ruleSet.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Cross-tenant RuleSet create rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}, ruleSetPublicId={RuleSetPublicId}",
                _tenantContext.TenantId, ruleSet.TenantId, ruleSet.PublicId);
            throw new InvalidOperationException("Cross-tenant RuleSet create rejected");
        }

        _db.RuleSets.Add(ruleSet);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Strict tenant guard — no SystemAdmin bypass on writes to prevent mis-tagging.
    /// </summary>
    public async Task UpdateAsync(RuleSet ruleSet, CancellationToken ct = default)
    {
        if (ruleSet.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Cross-tenant RuleSet update rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}, ruleSetPublicId={RuleSetPublicId}",
                _tenantContext.TenantId, ruleSet.TenantId, ruleSet.PublicId);
            throw new InvalidOperationException("Cross-tenant RuleSet update rejected");
        }

        if (_db.Entry(ruleSet).State == EntityState.Detached)
            _db.RuleSets.Update(ruleSet);

        await _db.SaveChangesAsync(ct);
    }
}
