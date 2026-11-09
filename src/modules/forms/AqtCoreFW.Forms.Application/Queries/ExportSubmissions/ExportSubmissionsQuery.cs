using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.ExportSubmissions;

/// <summary>
/// Exports all submissions for a given template as CSV, streamed line-by-line.
/// TemplatePublicId = FormTemplate.PublicId (T2 Guid AK — external addressing).
/// Header row = field keys from active template fields.
/// Data rows = one row per submission in SubmittedAt DESC order.
/// Only supports "csv" format — future formats can extend Format parameter.
///
/// Returns an <see cref="IAsyncEnumerable{T}"/> of CSV lines (header first, then one line per
/// submission) so a large export is not buffered in memory. Template-not-found / no-fields validation
/// runs BEFORE any line is yielded, so the handler can still fail with 404 before streaming starts.
/// The backing DB reader is held open for the duration of enumeration — the caller must iterate to
/// completion (the controller writes each line straight to the HTTP response body).
/// </summary>
public sealed record ExportSubmissionsQuery(
    Guid TenantId,
    Guid TemplatePublicId,
    string Format = "csv") : IQuery<IAsyncEnumerable<string>>;
