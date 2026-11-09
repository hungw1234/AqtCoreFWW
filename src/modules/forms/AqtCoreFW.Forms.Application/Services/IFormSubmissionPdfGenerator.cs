namespace AqtCoreFW.Forms.Application.Services;

/// <summary>
/// Generates PDF bytes for a form submission export.
/// Implemented in Infrastructure (QuestPDF) — Application layer stays PDF-library-free.
/// </summary>
public interface IFormSubmissionPdfGenerator
{
    /// <param name="templateName">Human-readable form template name.</param>
    /// <param name="submissionId">Submission GUID for display.</param>
    /// <param name="templateVersion">Template version number.</param>
    /// <param name="status">Submission status string (Pending / Approved / Rejected).</param>
    /// <param name="submittedAt">UTC submission timestamp.</param>
    /// <param name="reviewComment">Optional reviewer comment.</param>
    /// <param name="fields">Ordered key→(labelVi, rawValue) pairs. Null values shown as empty string.</param>
    /// <param name="isTableMode">True when SubmissionData is null (Table-mode storage).</param>
    /// <param name="signerBlocks">
    /// Optional per-role signer evidence blocks rendered after the field-data table.
    /// Each entry: (RoleLabelVi, SignerSubject, VerifiedAt, Verdict).
    /// Null or empty = single-signer/no-signer path; output is byte-identical to the no-signerBlocks call.
    /// </param>
    byte[] Generate(
        string templateName,
        Guid submissionId,
        int templateVersion,
        string status,
        DateTimeOffset submittedAt,
        string? reviewComment,
        IReadOnlyList<(string Key, string LabelVi, string? Value)> fields,
        bool isTableMode,
        IReadOnlyList<(string RoleLabelVi, string? SignerSubject, DateTimeOffset? VerifiedAt, bool? Verdict)>? signerBlocks = null);
}
