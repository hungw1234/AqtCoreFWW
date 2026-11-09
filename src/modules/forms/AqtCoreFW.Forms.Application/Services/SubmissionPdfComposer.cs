using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Services;

/// <summary>
/// Loads a form submission + template field labels (Dapper read-side) and generates PDF bytes
/// via <see cref="IFormSubmissionPdfGenerator"/>. Extracted from the export-PDF query to allow
/// reuse by the sign-document command without duplicating the Dapper SQL or field-merge logic.
///
/// Both callers get the same byte output; the export query downloads them directly,
/// the sign command stages them to the signing-original bucket before initiating a signing request.
/// </summary>
public sealed class SubmissionPdfComposer(
    IReadDbConnection db,
    IFormSubmissionPdfGenerator pdfGenerator)
    : ISubmissionPdfComposer
{
    public async Task<Result<ComposedSubmissionPdf>> ComposeAsync(
        Guid submissionPublicId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var row = await FetchSubmissionAsync(submissionPublicId, tenantId, cancellationToken);
        if (row is null)
            return Result.Fail(new NotFoundError($"FormSubmission {submissionPublicId} không tìm thấy."));

        var isTableMode = row.SubmissionData is null;
        var fieldLabels = await FetchFieldLabelsAsync(row.FormTemplateId, cancellationToken);
        var fields = BuildFieldList(row.SubmissionData, fieldLabels);

        var bytes = pdfGenerator.Generate(
            templateName: row.TemplateName,
            submissionId: row.Id,
            templateVersion: row.FormTemplateVersion,
            status: row.Status,
            submittedAt: row.SubmittedAt,
            reviewComment: row.ReviewComment,
            fields: fields,
            isTableMode: isTableMode);

        return Result.Ok(new ComposedSubmissionPdf(bytes, submissionPublicId));
    }

    private async Task<SubmissionPdfRow?> FetchSubmissionAsync(
        Guid submissionPublicId,
        Guid tenantId,
        CancellationToken ct)
    {
        // Query by PublicId (T2 external AK). Alias PublicId AS Id for SubmissionPdfRow.
        const string sql = """
            SELECT s.PublicId AS Id, s.FormTemplateId, s.FormTemplateVersion,
                   s.SubmittedBy, s.SubmittedAt, s.SubmissionData,
                   s.Status, s.ReviewedBy, s.ReviewedAt, s.ReviewComment,
                   t.Name AS TemplateName
            FROM forms.FormSubmissions s
            INNER JOIN forms.FormTemplates t ON t.Id = s.FormTemplateId
            WHERE s.PublicId = @SubmissionId AND s.TenantId = @TenantId AND s.IsDeleted = 0
            """;

        return await db.QueryFirstOrDefaultAsync<SubmissionPdfRow>(
            sql, new { SubmissionId = submissionPublicId, TenantId = tenantId }, ct);
    }

    // Fetch Vietnamese labels for all active fields of the template.
    // templateId = long (T1 internal PK).
    private async Task<IDictionary<string, string>> FetchFieldLabelsAsync(
        long templateId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT FieldKey, LabelVi
            FROM forms.FormFields
            WHERE FormTemplateId = @TemplateId
              AND IsActive = 1
            """;

        var rows = await db.QueryAsync<FieldLabelRow>(sql, new { TemplateId = templateId }, ct);
        return rows.ToDictionary(r => r.FieldKey, r => r.LabelVi);
    }

    // Merge JSON submission data with field labels; graceful on parse failures.
    private static IReadOnlyList<(string Key, string LabelVi, string? Value)> BuildFieldList(
        string? submissionData,
        IDictionary<string, string> fieldLabels)
    {
        if (string.IsNullOrWhiteSpace(submissionData))
            return [];

        IDictionary<string, object?> data;
        try
        {
            data = JsonSerializer.Deserialize<Dictionary<string, object?>>(submissionData)
                   ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            data = new Dictionary<string, object?>();
        }

        return data
            .Select(kv =>
            {
                fieldLabels.TryGetValue(kv.Key, out var label);
                return (kv.Key, LabelVi: label ?? kv.Key, Value: kv.Value?.ToString());
            })
            .ToList();
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization.
    // Id = PublicId (Guid, T2 external AK). FormTemplateId = long (T1 internal PK).
    private sealed class SubmissionPdfRow
    {
        public Guid Id { get; set; }           // PublicId aliased as Id
        // arch:suppress R_T2FK FormTemplate: private inner Dapper materializer (SubmissionPdfRow) — internal long PK projection
        public long FormTemplateId { get; set; }
        public int FormTemplateVersion { get; set; }
        public Guid SubmittedBy { get; set; }
        public DateTimeOffset SubmittedAt { get; set; }
        public string? SubmissionData { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? ReviewedBy { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string? ReviewComment { get; set; }
        public string TemplateName { get; set; } = string.Empty;
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization.
    private sealed class FieldLabelRow
    {
        public string FieldKey { get; set; } = string.Empty;
        public string LabelVi { get; set; } = string.Empty;
    }
}
