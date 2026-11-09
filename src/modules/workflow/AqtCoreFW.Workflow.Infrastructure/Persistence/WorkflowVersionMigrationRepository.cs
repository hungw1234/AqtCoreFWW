using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IWorkflowVersionMigrationRepository.
/// T1 entity: IRepository key is long (bigint PK). GenericRepository&lt;T, long&gt;.
/// Phase-4b B4c: changed from GenericRepository&lt;T, Guid&gt; to long.
///   GetByIdAsync: id changed Guid→long.
///   GetActiveOnSourceAsync: sourceDefinitionId changed Guid→long (WorkflowVersionMigration.SourceDefinitionId is long FK).
///   ListByDefinitionKeyAsync: queries WorkflowDefinition by DefinitionKey → resolves long PKs for source/target filters.
///   ListItemsAsync: migrationId changed Guid→long.
/// All tenant-scoped queries include TenantId filter for multi-tenant safety.
/// </summary>
public sealed class WorkflowVersionMigrationRepository(WorkflowDbContext context)
    : GenericRepository<WorkflowVersionMigration, long>(context), IWorkflowVersionMigrationRepository
{
    private WorkflowDbContext Ctx => (WorkflowDbContext)Context;

    // Active statuses used for rate-limit and duplicate checks
    private static readonly MigrationStatus[] ActiveStatuses =
    [
        MigrationStatus.PendingApproval,
        MigrationStatus.Approved,
        MigrationStatus.InProgress
    ];

    /// <inheritdoc/>
    public new async Task<Result<WorkflowVersionMigration>> GetByIdAsync(
        long id,
        CancellationToken ct = default)
    {
        var migration = await Ctx.VersionMigrations
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        return migration is null
            ? Result.Fail($"WorkflowVersionMigration '{id}' not found.")
            : Result.Ok(migration);
    }

    /// <inheritdoc/>
    public async Task<WorkflowVersionMigration?> GetActiveOnSourceAsync(
        long sourceDefinitionInternalId,
        Guid tenantId,
        CancellationToken ct = default) =>
        await Ctx.VersionMigrations
            .Where(m => m.SourceDefinitionId == sourceDefinitionInternalId
                     && m.TenantId == tenantId
                     && ActiveStatuses.Contains(m.Status))
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc/>
    public async Task<int> CountActiveByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default) =>
        await Ctx.VersionMigrations
            .CountAsync(m => m.TenantId == tenantId
                          && ActiveStatuses.Contains(m.Status), ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowVersionMigration>> ListByDefinitionKeyAsync(
        string definitionKey,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // Resolve definition key → internal long PKs (T2 entity pattern)
        var sourceInternalIds = await Ctx.Query<WorkflowDefinition>()
            .Where(d => d.DefinitionKey == definitionKey && d.TenantId == tenantId)
            .Select(d => d.Id)
            .ToListAsync(ct);

        return await Ctx.VersionMigrations
            .Where(m => m.TenantId == tenantId
                     && (sourceInternalIds.Contains(m.SourceDefinitionId)
                      || sourceInternalIds.Contains(m.TargetDefinitionId)))
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<WorkflowVersionMigrationItem> Items, int TotalCount)> ListItemsAsync(
        long migrationInternalId,
        MigrationItemStatus? statusFilter,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = Ctx.VersionMigrationItems
            .Where(i => i.MigrationId == migrationInternalId);

        if (statusFilter.HasValue)
            query = query.Where(i => i.Status == statusFilter.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(i => i.ProcessedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
