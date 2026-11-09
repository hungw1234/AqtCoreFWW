using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Infrastructure.Repositories;

/// <summary>
/// EF Core write-side repository for the DecisionTable entity.
/// Query handlers bypass this and use IReadDbConnection (Dapper) directly.
/// Defence-in-depth: inline tenant filter on reads + strict guards on writes.
/// </summary>
public sealed class DecisionTableRepository : IDecisionTableRepository
{
    private readonly RulesDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<DecisionTableRepository> _logger;

    public DecisionTableRepository(
        RulesDbContext db,
        ITenantContext tenantContext,
        ILogger<DecisionTableRepository> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Returns DecisionTable by long Id (T1 internal PK).
    /// Inline tenant filter: SystemAdmin bypasses; regular users see own tenant only.
    /// </summary>
    public async Task<DecisionTable?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.DecisionTables
            .Where(dt => _tenantContext.IsSystemAdmin || dt.TenantId == _tenantContext.TenantId)
            .FirstOrDefaultAsync(dt => dt.Id == id, ct);

    /// <summary>
    /// Strict tenant guard — no SystemAdmin bypass on writes to prevent mis-tagging.
    /// </summary>
    public async Task AddAsync(DecisionTable decisionTable, CancellationToken ct = default)
    {
        if (decisionTable.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Cross-tenant DecisionTable create rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}, decisionTableId={DecisionTableId}",
                _tenantContext.TenantId, decisionTable.TenantId, decisionTable.Id);
            throw new InvalidOperationException("Cross-tenant DecisionTable create rejected");
        }

        _db.DecisionTables.Add(decisionTable);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Strict tenant guard — no SystemAdmin bypass on writes to prevent mis-tagging.
    /// </summary>
    public async Task UpdateAsync(DecisionTable decisionTable, CancellationToken ct = default)
    {
        if (decisionTable.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Cross-tenant DecisionTable update rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}, decisionTableId={DecisionTableId}",
                _tenantContext.TenantId, decisionTable.TenantId, decisionTable.Id);
            throw new InvalidOperationException("Cross-tenant DecisionTable update rejected");
        }

        if (_db.Entry(decisionTable).State == EntityState.Detached)
            _db.DecisionTables.Update(decisionTable);

        await _db.SaveChangesAsync(ct);
    }
}
