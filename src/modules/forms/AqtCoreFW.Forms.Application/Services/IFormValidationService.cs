using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;

namespace AqtCoreFW.Forms.Application.Services;

/// <summary>
/// Validates a form submission's data against the template's active FormField entities.
/// Returns empty list on success; list of ValidationError per failing field on failure.
/// </summary>
public interface IFormValidationService
{
    /// <param name="tenantId">
    /// M1 cascade membership guard (OPTIONAL, default null ⇒ guard skipped, backward-safe): tenant scope for
    /// server-side re-resolve of in-row cascade children. When provided AND the service was constructed with a
    /// data-source resolver factory, the guard iterates every TableField row, re-resolves each cascade child's
    /// option-set from the row's parent value, and rejects Enum/Internal child values not in that set (422).
    /// External resolver failures soft-log and let the submit through (per Phase-06 decision).
    /// </param>
    Task<Result<List<ValidationError>>> ValidateAsync(
        IEnumerable<FormField> fields,
        IDictionary<string, object?> data,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default);
}
