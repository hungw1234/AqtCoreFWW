using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;

namespace AqtCoreFW.Forms.Domain.Repositories;

/// <summary>FormField repository — CRUD for field management commands.</summary>
public interface IFormFieldRepository : IRepository<FormField, long>
{
    /// <summary>Get all fields for a template, optionally filtering to active-only.</summary>
    Task<IReadOnlyList<FormField>> GetByTemplateIdAsync(
        long templateId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);
}
