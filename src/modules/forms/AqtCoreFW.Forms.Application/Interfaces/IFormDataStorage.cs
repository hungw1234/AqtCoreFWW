using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Forms.Application.Interfaces;

/// <summary>
/// Strategy interface for dual-mode form submission storage.
/// Json mode: reads/writes FormSubmission.SubmissionData (EF Core).
/// Table mode: reads/writes forms.Submissions_{templateId:N} (Dapper dynamic DDL).
/// templateId = FormTemplate.Id (long, T1 internal PK).
/// submissionId = FormSubmission.PublicId (Guid, T2 external AK).
/// </summary>
public interface IFormDataStorage
{
    /// <summary>
    /// Persist form data. Called after FormSubmission EF record is saved.
    /// For Json mode this is a no-op (data already in SubmissionData).
    /// For Table mode inserts a row into the dynamic table.
    /// </summary>
    Task WriteAsync(
        long templateId,
        Guid submissionId,
        Guid tenantId,
        int version,
        IDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>Read a single submission's field data by submission PublicId.</summary>
    Task<IDictionary<string, object?>?> ReadAsync(
        long templateId,
        Guid submissionId,
        CancellationToken cancellationToken = default);

    /// <summary>Paged query of all submissions for a template within a tenant.</summary>
    Task<PagedResult<IDictionary<string, object?>>> QueryAsync(
        long templateId,
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// RTBF: replace values of IsPii=true fields with "[DA AN DANH]" for the given submissions.
    /// Json mode: no-op (anonymization done on FormSubmission.SubmissionData by FormsPiiAnonymizer).
    /// Table mode: issues Dapper UPDATE on dynamic table for each piiFieldKey.
    /// Must be idempotent — safe to call multiple times on already-anonymized rows.
    /// </summary>
    Task AnonymizeAsync(
        long templateId,
        IReadOnlyList<Guid> submissionIds,
        IReadOnlyList<string> piiFieldKeys,
        CancellationToken cancellationToken = default);
}
