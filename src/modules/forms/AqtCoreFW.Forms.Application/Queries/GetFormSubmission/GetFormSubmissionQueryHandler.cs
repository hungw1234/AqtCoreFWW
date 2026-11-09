using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Queries.GetFormSubmission;

public sealed class GetFormSubmissionQueryHandler(
    IReadDbConnection db,
    IFormDataStorageFactory storageFactory,
    ITableFieldDataRepository tableFieldDataRepository)
    : IRequestHandler<GetFormSubmissionQuery, Result<FormSubmissionDto>>
{
    public async Task<Result<FormSubmissionDto>> Handle(
        GetFormSubmissionQuery request,
        CancellationToken cancellationToken)
    {
        // Load submission record + template StorageMode and PublicId in one query.
        // s.Id = FormSubmission.PublicId (Guid, T2 external AK).
        // s.FormTemplateId = FormTemplate.Id (long, T1 internal PK — kept for storage.ReadAsync + AttachTableFieldRows).
        // t.PublicId AS FormTemplatePublicId = T2 external AK projected to DTO (Phase-07 O-2).
        // s.SigningRequestId: correlation to the USB signing request (null when no signing initiated).
        //   The signed-PDF key is NOT stored on this row — read via signing request projection (pull-on-poll).
        const string sql = """
            SELECT s.PublicId AS Id, s.TenantId, s.FormTemplateId, s.FormTemplateVersion,
                   s.SubmittedBy, s.SubmittedAt, s.SubmissionData,
                   s.Status, s.ReviewedBy, s.ReviewedAt, s.ReviewComment,
                   s.SigningRequestId,
                   t.StorageMode, t.PublicId AS FormTemplatePublicId
            FROM forms.FormSubmissions s
            INNER JOIN forms.FormTemplates t ON t.Id = s.FormTemplateId
            WHERE s.PublicId = @SubmissionId AND s.TenantId = @TenantId AND s.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<FormSubmissionRow>(
            sql, new { request.SubmissionId, request.TenantId }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError($"FormSubmission {request.SubmissionId} không tìm thấy."));

        // Route data read to correct storage backend
        IDictionary<string, object?> data;
        if (Enum.TryParse<StorageMode>(row.StorageMode, out var mode) && mode == StorageMode.Table)
        {
            var storage = storageFactory.Create(StorageMode.Table);
            // ReadAsync: templateId=long, submissionId=Guid (PublicId)
            data = await storage.ReadAsync(row.FormTemplateId, row.Id, cancellationToken)
                   ?? new Dictionary<string, object?>();

            // Phase 3: attach child rows for each TableField in this template
            await AttachTableFieldRowsAsync(
                row.FormTemplateId, row.Id, data, cancellationToken);
        }
        else
        {
            data = DeserializeJsonData(row.SubmissionData);
        }

        // FormTemplateId in DTO = FormTemplate.PublicId (Guid T2 AK, Phase-07 O-2).
        // SigningRequestId: correlation to the USB signing request; FE polls signing status separately.
        return Result.Ok(new FormSubmissionDto(
            row.Id, row.TenantId, row.FormTemplatePublicId, row.FormTemplateVersion,
            row.SubmittedBy, row.SubmittedAt, data,
            row.Status, row.ReviewedBy, row.ReviewedAt, row.ReviewComment,
            row.SigningRequestId));
    }

    /// <summary>
    /// Loads active TableField definitions for the template, then fetches child rows
    /// from each child table and merges them into the parent data dict under the field key.
    /// templateId = long (T1 PK), submissionId = Guid (T2 PublicId).
    /// </summary>
    private async Task AttachTableFieldRowsAsync(
        long templateId,
        Guid submissionId,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken)
    {
        // Query active TableField entries for this template (Id + FieldKey only)
        const string fieldSql = """
            SELECT f.Id, f.FieldKey
            FROM forms.FormFields f
            WHERE f.FormTemplateId = @templateId
              AND f.Type = @tableFieldType
              AND f.IsActive = 1
            """;

        var tableFields = await db.QueryAsync<TableFieldRow>(
            fieldSql,
            new { templateId, tableFieldType = (int)FormFieldType.TableField },
            cancellationToken);

        foreach (var tf in tableFields)
        {
            var rows = await tableFieldDataRepository.GetRowsAsync(
                templateId, tf.Id, submissionId, cancellationToken);

            // Attach as list of row dicts under the original field key
            data[tf.FieldKey] = rows;
        }
    }

    private static IDictionary<string, object?> DeserializeJsonData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                   ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    // Dapper requires parameterless constructor or exact-match positional ctor.
    // Id = PublicId (Guid, T2 external AK). FormTemplateId = long (T1 internal PK, kept for storage calls).
    // FormTemplatePublicId = FormTemplate.PublicId (Guid T2 AK) — projected to DTO (Phase-07 O-2).
    private sealed class FormSubmissionRow
    {
        public Guid Id { get; set; }                            // PublicId aliased as Id
        public Guid TenantId { get; set; }
        // arch:suppress R_T2FK FormTemplate: private inner Dapper materializer (FormSubmissionRow) — internal long PK projection
        public long FormTemplateId { get; set; }                // T1 internal PK — used by storage.ReadAsync + AttachTableFieldRows
        public Guid FormTemplatePublicId { get; set; }          // T2 AK — projected to DTO
        public int FormTemplateVersion { get; set; }
        public Guid SubmittedBy { get; set; }
        public DateTimeOffset SubmittedAt { get; set; }
        public string? SubmissionData { get; set; }
        public string StorageMode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid? ReviewedBy { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string? ReviewComment { get; set; }
        /// <summary>
        /// Correlation to the USB signing request initiated for this submission's PDF.
        /// Null when no signing request has been initiated. Signed-PDF key is read via the
        /// signing request projection (pull-on-poll), not stored on this row.
        /// </summary>
        public Guid? SigningRequestId { get; set; }
    }

    private sealed record TableFieldRow(long Id, string FieldKey);
}
