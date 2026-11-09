using AqtCoreFW.Forms.Domain.Entities;

namespace AqtCoreFW.Forms.Application.Interfaces;

/// <summary>
/// DDL abstraction for dynamic Table-mode form storage.
/// All operations are idempotent — safe to call multiple times.
/// templateId = FormTemplate.Id (long, T1 internal PK).
/// tableFieldId = FormField.Id (long, T1 internal PK).
/// </summary>
public interface IFormTableMigrator
{
    /// <summary>
    /// Create forms.Submissions_{templateId} with typed columns for each active field.
    /// No-op if table already exists.
    /// </summary>
    Task CreateTableAsync(
        long templateId,
        IEnumerable<FormField> fields,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ALTER TABLE ADD COLUMN for a new field added to an Active template.
    /// No-op if column already exists.
    /// </summary>
    Task AddColumnAsync(
        long templateId,
        FormField field,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// CREATE OR ALTER VIEW forms.v_{sanitized_code} joining dynamic table with FormSubmissions.
    /// Replaces existing view if present.
    /// </summary>
    Task CreateOrReplaceViewAsync(
        string code,
        long templateId,
        IEnumerable<FormField> activeFields,
        CancellationToken cancellationToken = default);

    // ── Phase 3: Child tables for TableField ────────────────────────────────

    /// <summary>
    /// Create forms.SubmTableRows_{templateId}_{tableFieldId} with typed columns for child fields.
    /// Includes FK to parent submissions table and index on SubmissionId.
    /// No-op if table already exists.
    /// </summary>
    Task CreateChildTableAsync(
        long templateId,
        FormField tableField,
        IEnumerable<FormField> childFields,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// CREATE OR ALTER VIEW forms.v_{code}_{fieldKey} joining child rows with FormSubmissions.
    /// Replaces existing view if present.
    /// </summary>
    Task CreateOrReplaceChildViewAsync(
        string templateCode,
        long templateId,
        FormField tableField,
        IEnumerable<FormField> childFields,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ALTER TABLE ADD COLUMN on the child table for a new child field added to an Active template.
    /// No-op if column already exists.
    /// tableFieldId = FormField.Id (long, T1 internal PK) of the parent TableField.
    /// </summary>
    Task AddChildColumnAsync(
        long templateId,
        long tableFieldId,
        FormField childField,
        CancellationToken cancellationToken = default);
}
