using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;

namespace AqtCoreFW.Forms.Domain.Repositories;

/// <summary>
/// ViewDefinition repository — write path only.
/// Query handlers use IReadDbConnection (Dapper) for reads.
/// </summary>
public interface IViewDefinitionRepository : IRepository<ViewDefinition, long>
{
    /// <summary>Get view with Columns and Filters eagerly loaded.</summary>
    Task<ViewDefinition?> GetByIdWithDetailsAsync(
        long id,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Get all views for a given entity type within a tenant (no navigation includes).</summary>
    Task<IReadOnlyList<ViewDefinition>> GetByEntityTypeAsync(
        string entityType,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Add a new ViewDefinition (with its Columns and Filters) to the store.</summary>
    new Task AddAsync(ViewDefinition entity, CancellationToken cancellationToken = default);
}
