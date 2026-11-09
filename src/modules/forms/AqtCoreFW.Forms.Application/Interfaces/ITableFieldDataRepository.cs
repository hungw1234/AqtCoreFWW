namespace AqtCoreFW.Forms.Application.Interfaces;

/// <summary>
/// Abstraction for reading/writing rows to child dynamic tables (forms.SubmTableRows_{templateId:N}_{fieldId:N}).
/// Used when StorageMode = Table and field type = TableField.
/// templateId = FormTemplate.Id (long, T1 internal PK).
/// tableFieldId = FormField.Id (long, T1 internal PK).
/// submissionId = FormSubmission.PublicId (Guid, T2 external AK).
/// </summary>
public interface ITableFieldDataRepository
{
    /// <summary>
    /// Insert rows into the child table for a TableField.
    /// Each row is a dictionary of child field key → value.
    /// Max 1000 rows per call enforced by implementation.
    /// </summary>
    Task InsertRowsAsync(
        long templateId,
        long tableFieldId,
        Guid submissionId,
        IReadOnlyList<IDictionary<string, object?>> rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read all rows for a TableField submission, ordered by RowIndex.
    /// Returns empty list if no rows exist (table field left blank).
    /// </summary>
    Task<IReadOnlyList<IDictionary<string, object?>>> GetRowsAsync(
        long templateId,
        long tableFieldId,
        Guid submissionId,
        CancellationToken cancellationToken = default);
}
