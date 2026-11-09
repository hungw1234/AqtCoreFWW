using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain.Repositories;

namespace AqtCoreFW.Forms.Domain.Repositories;

/// <summary>Form template repository — write path only. Query handlers use IReadDbConnection (Dapper).</summary>
public interface IFormTemplateRepository : IRepository<FormTemplate, long>
{
    /// <summary>Look up by public Guid AK (external addressing — controller routes, command handlers).</summary>
    Task<FormTemplate?> GetByPublicIdAsync(Guid publicId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Look up by public Guid AK with fields eagerly loaded (publish + field management via publicId).</summary>
    Task<FormTemplate?> GetByPublicIdWithFieldsAsync(Guid publicId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Look up by public Guid AK with signer roles eagerly loaded (signer-role authoring).</summary>
    Task<FormTemplate?> GetByPublicIdWithSignerRolesAsync(Guid publicId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Get active template by ID for submission (legacy active-only check).</summary>
    Task<FormTemplate?> GetActiveByIdAsync(long id, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Get template with its FormFields eagerly loaded (for field management and submission validation).</summary>
    Task<FormTemplate?> GetWithFieldsAsync(long id, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="GetWithFieldsAsync"/> but for the anonymous public path: pins
    /// SESSION_CONTEXT TenantId to the server-resolved tenant for the read, so a no-ambient-tenant
    /// caller still passes the standard FormTemplates RLS FILTER under a non-db_owner login.
    /// <paramref name="tenantId"/> MUST be server-resolved (from the by-Code lookup), never client-supplied.
    /// </summary>
    Task<FormTemplate?> GetPublicWithFieldsAsync(long id, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Get template by unique Code within a tenant.</summary>
    Task<FormTemplate?> GetByCodeAsync(string code, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Returns true if any non-deleted submission exists for this template.</summary>
    Task<bool> HasSubmissionsAsync(long templateId, CancellationToken cancellationToken = default);

    /// <summary>Get all templates in Materializing status (for recovery on startup).</summary>
    Task<IReadOnlyList<FormTemplate>> GetMaterializingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage a new FormTemplateVersion snapshot for INSERT without committing.
    /// SaveChanges happens in the same UoW as the calling UpdateAsync so the snapshot
    /// and the publish status flip persist atomically (or roll back together).
    /// </summary>
    Task AddVersionAsync(FormTemplateVersion version, CancellationToken cancellationToken = default);
}
