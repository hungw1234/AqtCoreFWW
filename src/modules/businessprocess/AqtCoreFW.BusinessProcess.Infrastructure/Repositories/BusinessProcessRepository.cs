using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Infrastructure.Repositories;

/// <summary>
/// EF Core write-side repository for the BusinessProcessDefinition aggregate.
/// Reads include StateForms + TransitionForms navigations (consumers requiring shallow lists hit IReadDbConnection).
/// Pattern A tenant guard (R13): inline filter on reads, strict throw on writes (no SystemAdmin bypass).
/// </summary>
public sealed class BusinessProcessRepository : IBusinessProcessRepository
{
    private readonly BusinessProcessDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<BusinessProcessRepository> _logger;

    public BusinessProcessRepository(
        BusinessProcessDbContext db,
        ITenantContext tenantContext,
        ILogger<BusinessProcessRepository> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<BusinessProcessDefinition?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default)
        => await _db.BusinessProcessDefinitions
            .Include(b => b.StateForms)
            .Include(b => b.TransitionForms)
            .Where(b => _tenantContext.IsSystemAdmin || b.TenantId == _tenantContext.TenantId)
            .FirstOrDefaultAsync(b => b.PublicId == publicId, ct);

    public async Task<BusinessProcessDefinition?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.BusinessProcessDefinitions
            .Include(b => b.StateForms)
            .Include(b => b.TransitionForms)
            .Where(b => _tenantContext.IsSystemAdmin || b.TenantId == _tenantContext.TenantId)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<IReadOnlyList<BusinessProcessDefinition>> GetActiveByTenantAsync(CancellationToken ct = default)
        => await _db.BusinessProcessDefinitions
            .AsNoTracking()
            .Where(b => b.IsActive)
            .Where(b => _tenantContext.IsSystemAdmin || b.TenantId == _tenantContext.TenantId)
            .OrderBy(b => b.Code)
            .ToListAsync(ct);

    public async Task<BusinessProcessDefinition?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await _db.BusinessProcessDefinitions
            .Include(b => b.StateForms)
            .Include(b => b.TransitionForms)
            .Where(b => _tenantContext.IsSystemAdmin || b.TenantId == _tenantContext.TenantId)
            .FirstOrDefaultAsync(b => b.Code == code, ct);

    public async Task AddAsync(BusinessProcessDefinition bp, CancellationToken ct = default)
    {
        if (bp.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Cross-tenant BusinessProcess create rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}, bpPublicId={BpPublicId}",
                _tenantContext.TenantId, bp.TenantId, bp.PublicId);
            throw new InvalidOperationException(
                $"{BusinessProcessErrorCodes.CrossTenantRejected}: Cross-tenant BusinessProcess create rejected.");
        }

        _db.BusinessProcessDefinitions.Add(bp);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(BusinessProcessDefinition bp, CancellationToken ct = default)
    {
        if (bp.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Cross-tenant BusinessProcess update rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}, bpPublicId={BpPublicId}",
                _tenantContext.TenantId, bp.TenantId, bp.PublicId);
            throw new InvalidOperationException(
                $"{BusinessProcessErrorCodes.CrossTenantRejected}: Cross-tenant BusinessProcess update rejected.");
        }

        // ReplaceFormMaps flow re-adds (state, form) / (transition, form) triples after Clear*.
        // EF Core 10 auto-marks severed required-relationship children Deleted in ChangeTracker
        // AND graph-walks the aggregate's nav properties at SaveChanges to discover new children
        // as Added. EF's modification-command batch sorter then emits INSERT *before* DELETE for
        // the same table, colliding with UX_*_Bp_(State|Transition)_Form unique-index (SQL 2601).
        // AutoDetectChangesEnabled=false does NOT disable graph-walk discovery — detaching the
        // Added children is futile because nav-walk re-discovers them every SaveChanges.
        //
        // T3-BP-FORMMAP-REPRO fix (2026-05-24): bypass EF tracker for orphan removal via
        // ExecuteDeleteAsync (parameterised, schema-agnostic), then let standard SaveChanges INSERT
        // the new children. Single SaveChanges call after orphans are physically gone → unique index
        // sees no collision. ClientCascade config (companion migration ChangeBpFormMapsCascadeToClientCascade)
        // removes DB-level ON DELETE CASCADE — BP soft-deletes only, so cascade was cosmetic.

        // Identify orphans: tracked children for this BP whose Id is NOT in the aggregate's
        // current StateForms/TransitionForms list. EF's auto-detect (triggered by Entries() access)
        // may already have marked them Deleted; accept either Unchanged or Deleted state since the
        // row still exists in DB and needs raw-SQL removal. Skip Id==0 (newly added, never persisted).
        var orphanStateFormIds = _db.ChangeTracker.Entries<BusinessProcessStateForm>()
            .Where(e => (e.State == EntityState.Unchanged || e.State == EntityState.Deleted)
                     && e.Entity.BusinessProcessDefinitionId == bp.Id
                     && e.Entity.Id > 0
                     && !bp.StateForms.Any(sf => sf.Id == e.Entity.Id))
            .Select(e => e.Entity.Id)
            .ToList();
        var orphanTransitionFormIds = _db.ChangeTracker.Entries<BusinessProcessTransitionForm>()
            .Where(e => (e.State == EntityState.Unchanged || e.State == EntityState.Deleted)
                     && e.Entity.BusinessProcessDefinitionId == bp.Id
                     && e.Entity.Id > 0
                     && !bp.TransitionForms.Any(tf => tf.Id == e.Entity.Id))
            .Select(e => e.Entity.Id)
            .ToList();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Phase 1: ExecuteDeleteAsync orphans (parameterised, bypasses EF tracker — no INSERT sneaks in)
        if (orphanStateFormIds.Count > 0)
        {
            await _db.Set<BusinessProcessStateForm>()
                .Where(e => orphanStateFormIds.Contains(e.Id))
                .ExecuteDeleteAsync(ct);
        }
        if (orphanTransitionFormIds.Count > 0)
        {
            await _db.Set<BusinessProcessTransitionForm>()
                .Where(e => orphanTransitionFormIds.Contains(e.Id))
                .ExecuteDeleteAsync(ct);
        }

        // Detach orphans from tracker so SaveChanges doesn't try to re-DELETE rows already gone
        foreach (var entry in _db.ChangeTracker.Entries<BusinessProcessStateForm>()
            .Where(e => orphanStateFormIds.Contains(e.Entity.Id)).ToList())
            entry.State = EntityState.Detached;
        foreach (var entry in _db.ChangeTracker.Entries<BusinessProcessTransitionForm>()
            .Where(e => orphanTransitionFormIds.Contains(e.Entity.Id)).ToList())
            entry.State = EntityState.Detached;

        if (_db.Entry(bp).State == EntityState.Detached)
            _db.BusinessProcessDefinitions.Update(bp);

        // Phase 2: standard SaveChanges — EF discovers new children via graph-walk + INSERTs only.
        // Unique index now sees no colliding orphan row (Phase 1 deleted them).
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }
}
