using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// EF Core repository for WorkflowTask aggregate.
/// T1 entity: IRepository key is long (bigint PK). GenericRepository&lt;T, long&gt;.
/// Phase-4b B4c: changed from GenericRepository&lt;T, Guid&gt; to long.
///   GetByInstanceAsync: param changed Guid→long (WorkflowTask.WorkflowInstanceId is long FK).
///   GetByBranchChildStatusIdAsync: param changed Guid→long (BranchChildStatusId is long FK).
///   GetByIdAndTenantAsync: id changed Guid→long.
/// P4 Slice 4-C: GetInboxAsync (EXISTS-over-nav-prop DCAND-a payoff) +
///   UpdateWithClaimGuardAsync M1 exception-discrimination (TaskFinalState vs AlreadyClaimed).
/// </summary>
public sealed class WorkflowTaskRepository(WorkflowDbContext context)
    : GenericRepository<WorkflowTask, long>(context), IWorkflowTaskRepository
{
    private readonly WorkflowDbContext _context = context;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowTask>> GetByInstanceAsync(
        long instanceInternalId,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowTasks
            .Where(t => t.WorkflowInstanceId == instanceInternalId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowTask>> GetOpenNonBranchByInstanceAsync(
        long instanceInternalId,
        CancellationToken cancellationToken = default)
    {
        // Tracked (no AsNoTracking) — caller mutates Complete() and flushes in its own SaveChanges.
        return await _context.WorkflowTasks
            .Where(t => t.WorkflowInstanceId == instanceInternalId
                     && t.BranchChildStatusId == null
                     && (t.Status == WorkflowTaskStatus.Open
                      || t.Status == WorkflowTaskStatus.InProgress))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<WorkflowTask?> GetLastCompletedTaskAsync(
        long instanceInternalId,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowTasks
            .AsNoTracking()
            .Where(t => t.WorkflowInstanceId == instanceInternalId
                     && t.CompletedAt != null)
            .OrderByDescending(t => t.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowTask>> GetByAssigneeAsync(
        Guid assigneeId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowTasks
            .Where(t => t.AssigneeId == assigneeId
                     && t.TenantId == tenantId
                     && (t.Status == WorkflowTaskStatus.Open
                      || t.Status == WorkflowTaskStatus.InProgress))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<WorkflowTask?> GetByBranchChildStatusIdAsync(
        long branchChildStatusInternalId,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowTasks
            .Where(t => t.BranchChildStatusId == branchChildStatusInternalId
                     && t.Status != WorkflowTaskStatus.Cancelled)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<WorkflowTask>> GetByIdAndTenantAsync(
        long id,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // FirstOrDefaultAsync respects global query filters (soft-delete, tenant).
        // Explicit TenantId predicate prevents cross-tenant access even if filters are bypassed.
        var task = await _context.WorkflowTasks
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

        return task is null
            ? Result.Fail(new NotFoundError($"WorkflowTask '{id}' not found."))
            : Result.Ok(task);
    }

    /// <inheritdoc/>
    public void Stage(WorkflowTask task)
    {
        // Enlist in ChangeTracker without saving. The shared WorkflowDbContext (scoped per request)
        // means IWorkflowInstanceRepository.SaveChangesAsync flushes this alongside the instance
        // state update in one round-trip (S7 single-SaveChanges invariant).
        _context.WorkflowTasks.Add(task);
    }

    /// <inheritdoc/>
    public void StageCandidateIndex(IReadOnlyList<WorkflowTaskCandidateIndex> rows)
    {
        // Enlist without saving. Each row's WorkflowTask nav-prop points to the parent
        // staged above, so EF relationship-fixup propagates the IDENTITY-generated task.Id
        // to each row's WorkflowTaskId FK at SaveChanges time (S7 invariant preserved).
        _context.WorkflowTaskCandidateIndex.AddRange(rows);
    }

    /// <inheritdoc/>
    public async Task<bool> IsPoolEligibleAsync(
        long taskId,
        IReadOnlySet<string> permissionCodes,
        IReadOnlySet<string> roleCodes,
        IReadOnlySet<string> groupCodes,
        IReadOnlySet<string> orgUnitCodes,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // PrincipalType ∈ {Permission, Role, Group, OrgUnit} — RT2-15 Phase 04 adds OrgUnit arm.
        // Explicit TenantId predicate = defense-in-depth per RT2-8 (low-cardinality refs collide
        // across tenants; RLS alone is insufficient).
        // AnyAsync terminates at the first matching row — index seek on
        // IX_WorkflowTaskCandidateIndex_Tenant_Principal (TenantId, PrincipalType, PrincipalRefId).
        // Empty set on any side → EF Contains() translates to "1=0" short-circuit — that arm
        // contributes no rows but the OR still evaluates the other arms cleanly.
        return await _context.WorkflowTaskCandidateIndex
            .AnyAsync(r =>
                    r.WorkflowTaskId == taskId
                    && r.TenantId == tenantId
                    && ((r.PrincipalType == CandidatePrincipalType.Permission
                            && permissionCodes.Contains(r.PrincipalRefId))
                        ||
                        (r.PrincipalType == CandidatePrincipalType.Role
                            && roleCodes.Contains(r.PrincipalRefId))
                        ||
                        (r.PrincipalType == CandidatePrincipalType.Group
                            && groupCodes.Contains(r.PrincipalRefId))
                        ||
                        (r.PrincipalType == CandidatePrincipalType.OrgUnit
                            && orgUnitCodes.Contains(r.PrincipalRefId))),
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateWithClaimGuardAsync(
        WorkflowTask task,
        CancellationToken cancellationToken = default)
    {
        // Same attach-guard as GenericRepository.UpdateAsync — only attach if detached.
        if (_context.Entry(task).State == EntityState.Detached)
            _context.Set<WorkflowTask>().Update(task);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            // P4 4-C M1: Discriminate between two concurrent-race failure modes:
            //   (a) Task reached a terminal state (Completed/Cancelled) — another operation finished it.
            //   (b) Task was claimed by another concurrent caller — canonical AlreadyClaimed path.
            //
            // Re-query with AsNoTracking + IgnoreQueryFilters: the EF context is inconsistent
            // after SaveChanges throws (stale tracked entity). AsNoTracking bypasses the broken
            // tracked state cleanly. IgnoreQueryFilters bypasses tenant + soft-delete global
            // filters — defensive against captured-context drift (e.g., model-cache contamination
            // in multi-context test scenarios, or expired scoped tenant context post-exception).
            // We already know task.Id and the row was tracked under the caller's tenant, so the
            // tenant check is redundant here; bypassing it makes the discrimination robust.
            // D1-α zero-EF-leak: no EF types cross to Application layer.
            var current = await _context.WorkflowTasks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == task.Id, cancellationToken);

            return current?.Status switch
            {
                WorkflowTaskStatus.Done =>
                    Result.Fail(WorkflowErrors.TaskFinalState(task.Id, "Done")),
                WorkflowTaskStatus.Cancelled =>
                    Result.Fail(WorkflowErrors.TaskFinalState(task.Id, "Cancelled")),
                // Null (row deleted) or any active status → another caller won the claim race.
                _ => Result.Fail(WorkflowErrors.AlreadyClaimed(task.Id))
            };
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowTaskDto>> GetInboxAsync(
        Guid userId,
        IReadOnlySet<string> permissionCodes,
        IReadOnlySet<string> roleCodes,
        IReadOnlySet<string> groupCodes,
        IReadOnlySet<string> orgUnitCodes,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // DCAND-a payoff: EXISTS subquery over WorkflowTaskCandidateIndex FK nav-prop.
        // Two-arm OR:
        //   (a) Task is directly assigned to the caller (AssigneeId = userId).
        //   (b) Task belongs to a pool the caller can claim — EXISTS on CandidateIndex with either
        //       (PrincipalType=Permission ∧ PrincipalRefId ∈ permCodes) OR
        //       (PrincipalType=Role       ∧ PrincipalRefId ∈ roleCodes) OR
        //       (PrincipalType=Group      ∧ PrincipalRefId ∈ groupCodes) OR
        //       (PrincipalType=OrgUnit    ∧ PrincipalRefId ∈ orgUnitCodes).  — RT2-15 Phase 04.
        //
        // Defense-in-depth: explicit TenantId predicate on BOTH the task and candidate-index rows
        // (RT2-8: low-cardinality refs can collide across tenants; RLS alone is insufficient).
        //
        // WorkflowInstanceId is Guid (T2 PublicId AK): projected via JOIN to WorkflowInstances
        // because WorkflowTask.WorkflowInstanceId is a long FK (no nav-prop to WorkflowInstance).
        //
        // AsNoTracking: read-only projection; no change tracking needed for DTO queries.
        // EF translates permissionCodes.Contains(c.PrincipalRefId) → SQL IN (...).
        // Empty sets across all 4 → all arms constant-false → only assignee arm.

        return await _context.WorkflowTasks
            .AsNoTracking()
            .Where(t =>
                t.TenantId == tenantId
                && (t.Status == WorkflowTaskStatus.Open || t.Status == WorkflowTaskStatus.InProgress)
                && (t.AssigneeId == userId
                    || _context.WorkflowTaskCandidateIndex.Any(c =>
                        c.WorkflowTaskId == t.Id
                        && c.TenantId == tenantId
                        && ((c.PrincipalType == CandidatePrincipalType.Permission
                                && permissionCodes.Contains(c.PrincipalRefId))
                            ||
                            (c.PrincipalType == CandidatePrincipalType.Role
                                && roleCodes.Contains(c.PrincipalRefId))
                            ||
                            (c.PrincipalType == CandidatePrincipalType.Group
                                && groupCodes.Contains(c.PrincipalRefId))
                            ||
                            (c.PrincipalType == CandidatePrincipalType.OrgUnit
                                && orgUnitCodes.Contains(c.PrincipalRefId))))))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Join(
                _context.WorkflowInstances.AsNoTracking(),
                t => t.WorkflowInstanceId,
                wi => wi.Id,
                (t, wi) => new WorkflowTaskDto(
                    t.Id,
                    wi.PublicId,
                    t.Title,
                    t.Description,
                    t.AssigneeId,
                    t.Status,
                    t.Priority,
                    t.DueDate,
                    t.CompletedAt,
                    t.CompletedBy,
                    t.TenantId,
                    t.CreatedAt,
                    t.CreatedBy,
                    t.DelegatedFromUserId))
            .ToListAsync(cancellationToken);
    }
}
