using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Write-side repository for WorkflowVersionMigration aggregate.
/// Used by Create/Approve/Execute/Cancel command handlers.
/// Phase-4b B4c: T1 entity — IRepository&lt;T, long&gt; key is internal bigint PK.
/// </summary>
public interface IWorkflowVersionMigrationRepository : IRepository<WorkflowVersionMigration, long>
{
    /// <summary>Load migration by ID (long bigint PK) with no navigation properties.</summary>
    new Task<Result<WorkflowVersionMigration>> GetByIdAsync(
        long id,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the first active migration on a source definition (by internal long Id).
    /// "Active" = Status ∈ {PendingApproval, Approved, InProgress}.
    /// Used by Create handler to enforce no-duplicate-pending rule.
    /// </summary>
    Task<WorkflowVersionMigration?> GetActiveOnSourceAsync(
        long sourceDefinitionInternalId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Count of active migrations for a tenant.
    /// Used by Create handler to enforce rate-limit ≤ 3 per tenant (M10).
    /// </summary>
    Task<int> CountActiveByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all migrations targeting a definition key (source or target side).
    /// Used by GetMigrationsByDefinition query handler.
    /// </summary>
    Task<IReadOnlyList<WorkflowVersionMigration>> ListByDefinitionKeyAsync(
        string definitionKey,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns paged migration items for a migration (by internal long migration Id).
    /// Used by GetMigrationItems query handler (avoids 10k-row FE load).
    /// </summary>
    Task<(IReadOnlyList<WorkflowVersionMigrationItem> Items, int TotalCount)> ListItemsAsync(
        long migrationInternalId,
        MigrationItemStatus? statusFilter,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
