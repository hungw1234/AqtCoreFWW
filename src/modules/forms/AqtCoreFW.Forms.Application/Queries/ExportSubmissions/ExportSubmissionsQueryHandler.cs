using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Queries.ExportSubmissions;

/// <summary>
/// Streams a UTF-8 CSV (header line + one line per submission) from template field headers +
/// submission data rows. Uses the Dapper read connection's unbuffered stream so a large export never
/// materializes every row at once — the previous implementation held the whole result set plus a full
/// CSV string in memory (tens of MB for 10k rows).
///
/// Only JSON-mode submissions are supported (SubmissionData column). Table-mode submissions fallback:
/// writes empty cells for table-stored fields.
///
/// Header/field resolution (and its 404s) runs in <see cref="Handle"/> BEFORE the first line is
/// yielded, so a missing template still fails with NotFound before streaming begins.
/// </summary>
public sealed class ExportSubmissionsQueryHandler(IReadDbConnection db)
    : IRequestHandler<ExportSubmissionsQuery, Result<IAsyncEnumerable<string>>>
{
    public async Task<Result<IAsyncEnumerable<string>>> Handle(
        ExportSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        // Resolve public Guid AK → internal long Id (FormSubmissions.FormTemplateId = long FK)
        const string resolveSql = """
            SELECT Id FROM forms.FormTemplates
            WHERE PublicId = @TemplatePublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var internalId = await db.QueryFirstOrDefaultAsync<long?>(
            resolveSql, new { request.TemplatePublicId, request.TenantId }, cancellationToken);

        if (internalId is null)
            return Result.Fail(new NotFoundError(
                $"FormTemplate {request.TemplatePublicId} không tồn tại."));

        // Load active field keys for column headers (ordered by DisplayOrder). Buffered read: the field
        // set is small and needed up-front to build both the header line and every data line.
        const string fieldsSql = """
            SELECT f.FieldKey, f.LabelEn
            FROM forms.FormFields f
            INNER JOIN forms.FormTemplates t ON t.Id = f.FormTemplateId
            WHERE f.FormTemplateId = @InternalId
              AND t.TenantId = @TenantId
              AND f.IsActive = 1
              AND f.ParentFieldId IS NULL
            ORDER BY f.DisplayOrder
            """;

        var fields = (await db.QueryAsync<FieldHeader>(
            fieldsSql,
            new { InternalId = internalId.Value, request.TenantId },
            cancellationToken)).ToList();

        if (fields.Count == 0)
            return Result.Fail(new NotFoundError(
                $"FormTemplate {request.TemplatePublicId} không có fields."));

        // Validation passed → return the streaming producer. No line is yielded until the controller
        // starts enumerating; the DB reader is opened lazily inside StreamCsvLines.
        return Result.Ok(StreamCsvLines(internalId.Value, request.TenantId, fields, cancellationToken));
    }

    /// <summary>
    /// Yields the CSV header line, then one line per submission streamed unbuffered from the DB.
    /// Tenant isolation is param-based (WHERE s.TenantId = @TenantId), not RLS-carve-out, so streaming
    /// preserves it without a bypass variant.
    /// </summary>
    private async IAsyncEnumerable<string> StreamCsvLines(
        long internalId,
        Guid tenantId,
        List<FieldHeader> fields,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Header row: system columns + field labels
        yield return BuildHeaderLine(fields);

        const string submissionsSql = """
            SELECT s.Id, s.SubmittedBy, s.SubmittedAt, s.SubmissionData
            FROM forms.FormSubmissions s
            WHERE s.FormTemplateId = @InternalId
              AND s.TenantId = @TenantId
              AND s.IsDeleted = 0
            ORDER BY s.SubmittedAt DESC
            """;

        // Bounded command-timeout so a hung single-row read cannot pin the pooled connection
        // indefinitely (client-disconnect is otherwise observed only between rows — the Dapper
        // unbuffered API takes no CancellationToken). 120s covers a large 10k-row export.
        const int exportCommandTimeoutSeconds = 120;

        await foreach (var s in db.QueryUnbufferedAsync<SubmissionRow>(
            submissionsSql,
            new { InternalId = internalId, TenantId = tenantId },
            cancellationToken,
            commandTimeout: exportCommandTimeoutSeconds))
        {
            yield return BuildDataLine(fields, s);
        }
    }

    private static string BuildHeaderLine(List<FieldHeader> fields)
    {
        var sb = new System.Text.StringBuilder("SubmissionId,SubmittedBy,SubmittedAt");
        foreach (var f in fields)
        {
            sb.Append(',');
            sb.Append(EscapeCsvCell(f.LabelEn));
        }
        return sb.ToString();
    }

    private static string BuildDataLine(List<FieldHeader> fields, SubmissionRow s)
    {
        var data = DeserializeSubmissionData(s.SubmissionData);

        var sb = new System.Text.StringBuilder();
        sb.Append(EscapeCsvCell(s.Id.ToString()));
        sb.Append(',');
        sb.Append(EscapeCsvCell(s.SubmittedBy.ToString()));
        sb.Append(',');
        sb.Append(EscapeCsvCell(s.SubmittedAt.ToString("yyyy-MM-dd HH:mm:ss")));

        foreach (var f in fields)
        {
            sb.Append(',');
            var value = data.TryGetValue(f.FieldKey, out var v)
                ? v?.ToString() ?? string.Empty
                : string.Empty;
            sb.Append(EscapeCsvCell(value));
        }

        return sb.ToString();
    }

    /// <summary>Wraps value in quotes if it contains comma, quote, or newline; escapes inner quotes.</summary>
    private static string EscapeCsvCell(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static Dictionary<string, object?> DeserializeSubmissionData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record FieldHeader(string FieldKey, string LabelEn);

    private sealed record SubmissionRow(
        Guid Id,
        Guid SubmittedBy,
        DateTimeOffset SubmittedAt,
        string? SubmissionData);
}
