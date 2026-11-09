using System.Globalization;
using System.Text;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.ExportInstanceAudit;

/// <summary>
/// Builds an audit trail export for one workflow instance.
/// Format switch: JSON (serialize DTO) / CSV (manual build, no CsvHelper per YAGNI) / PDF (placeholder).
/// Tenant isolation enforced at SQL level (WHERE TenantId = @TenantId).
/// Max 10,000 transition rows per export (safety limit).
/// PDF format returns Failed — composer implementation deferred to a follow-up task.
/// </summary>
public sealed class ExportInstanceAuditQueryHandler(IReadDbConnection db)
    : IRequestHandler<ExportInstanceAuditQuery, Result<AuditExportResult>>
{
    private const int MaxTransitionRows = 10_000;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task<Result<AuditExportResult>> Handle(
        ExportInstanceAuditQuery request,
        CancellationToken ct)
    {
        var header = await FetchHeaderAsync(request.InstanceId, request.TenantId, ct);
        if (header is null)
            return Result.Fail(new NotFoundError(
                $"Workflow instance {request.InstanceId} not found."));

        // Use internal long PK for FK-based sub-queries (h.InstanceId + t.WorkflowInstanceId are bigint FKs)
        var transitions = (await FetchTransitionsAsync(header.InternalInstanceId, ct)).ToList();
        var assignments = (await FetchTaskAssignmentsAsync(header.InternalInstanceId, ct)).ToList();

        var dto = BuildDto(header, transitions, assignments);

        return request.Format switch
        {
            AuditExportFormat.Json => Result.Ok(BuildJson(dto)),
            AuditExportFormat.Csv => Result.Ok(BuildCsv(dto)),
            AuditExportFormat.Pdf => Result.Fail(new ValidationError(
                "PDF export not yet implemented — use JSON or CSV.", "format")),
            _ => Result.Fail(new ValidationError(
                $"Unsupported format: {request.Format}.", "format")),
        };
    }

    private async Task<AuditHeaderRow?> FetchHeaderAsync(Guid instanceId, Guid tenantId, CancellationToken ct)
    {
        // T2 WorkflowInstance: filter by PublicId (Guid AK), project internal Id as InternalInstanceId
        // for downstream FK sub-queries (h.InstanceId + t.WorkflowInstanceId are bigint FKs).
        // PublicId surfaced as InstanceId in DTO (Guid external contract unchanged).
        const string sql = """
            SELECT
                i.Id       AS InternalInstanceId,
                i.PublicId AS InstanceId,
                i.TenantId,
                d.Name AS DefinitionName,
                d.DefinitionKey,
                i.EntityType,
                i.EntityId,
                i.StartedAt,
                i.CompletedAt,
                s.Name AS CurrentStateName,
                i.DueAt
            FROM workflow.WorkflowInstances i
            INNER JOIN workflow.WorkflowDefinitions d ON d.Id = i.DefinitionId
            INNER JOIN workflow.WorkflowStates s ON s.Id = i.CurrentStateId
            WHERE i.PublicId = @InstanceId AND i.TenantId = @TenantId
            """;

        return await db.QueryFirstOrDefaultAsync<AuditHeaderRow>(
            sql, new { InstanceId = instanceId, TenantId = tenantId }, ct);
    }

    // internalInstanceId is long (bigint PK) — h.InstanceId and t.WorkflowInstanceId are bigint FKs.
    private async Task<IEnumerable<AuditTransitionRow>> FetchTransitionsAsync(long internalInstanceId, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (@Max)
                fs.Name AS FromStateName,
                ts.Name AS ToStateName,
                h.ActionName,
                h.TransitionedBy,
                h.Comment,
                h.TransitionedAt,
                (SELECT COUNT(*) FROM workflow.WorkflowTransitionAttachments a WHERE a.InstanceHistoryId = h.InternalId) AS AttachmentCount
            FROM workflow.WorkflowInstanceHistories h
            INNER JOIN workflow.WorkflowStates fs ON fs.Id = h.FromStateId
            INNER JOIN workflow.WorkflowStates ts ON ts.Id = h.ToStateId
            WHERE h.InstanceId = @InstanceId
            ORDER BY h.TransitionedAt ASC
            """;

        return await db.QueryAsync<AuditTransitionRow>(
            sql, new { InstanceId = internalInstanceId, Max = MaxTransitionRows }, ct);
    }

    private async Task<IEnumerable<AuditTaskAssignmentRow>> FetchTaskAssignmentsAsync(long internalInstanceId, CancellationToken ct)
    {
        const string sql = """
            SELECT
                t.Title AS TaskTitle,
                a.AssignedToId,
                a.AssignedById,
                a.Type,
                a.AssignedAt,
                a.Reason
            FROM workflow.TaskAssignments a
            INNER JOIN workflow.WorkflowTasks t ON t.Id = a.WorkflowTaskId
            WHERE t.WorkflowInstanceId = @InstanceId
            ORDER BY a.AssignedAt ASC
            """;

        return await db.QueryAsync<AuditTaskAssignmentRow>(
            sql, new { InstanceId = internalInstanceId }, ct);
    }

    private static InstanceAuditDto BuildDto(
        AuditHeaderRow header,
        List<AuditTransitionRow> transitions,
        List<AuditTaskAssignmentRow> assignments)
    {
        var transitionDtos = transitions.Select(t => new AuditTransitionDto(
            t.FromStateName, t.ToStateName, t.ActionName,
            t.TransitionedBy, t.Comment, t.TransitionedAt, t.AttachmentCount)).ToList();

        var assignmentDtos = assignments.Select(a => new AuditTaskAssignmentDto(
            a.TaskTitle, a.AssignedToId, a.AssignedById, a.Type,
            a.AssignedAt, a.Reason)).ToList();

        return new InstanceAuditDto(
            header.InstanceId,
            header.TenantId,
            header.DefinitionName,
            header.DefinitionKey,
            header.EntityType,
            header.EntityId,
            header.StartedAt,
            header.CompletedAt,
            header.CurrentStateName,
            header.DueAt,
            transitionDtos,
            assignmentDtos);
    }

    private static AuditExportResult BuildJson(InstanceAuditDto dto)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOpts);
        var fileName = $"workflow-audit-{dto.InstanceId:N}.json";
        return new AuditExportResult(AuditExportFormat.Json, fileName, "application/json", bytes);
    }

    /// <summary>
    /// Build CSV for transitions only (task assignments omitted — flat CSV can't represent both).
    /// Use JSON format to get the full audit picture including task assignments.
    /// UTF-8 with BOM: required for Excel to correctly read Vietnamese diacritics on Windows.
    /// </summary>
    private static AuditExportResult BuildCsv(InstanceAuditDto dto)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FromState,ToState,Action,TransitionedBy,TransitionedAt,AttachmentCount,Comment");
        foreach (var t in dto.Transitions)
        {
            sb.Append(CsvField(t.FromStateName)).Append(',')
              .Append(CsvField(t.ToStateName)).Append(',')
              .Append(CsvField(t.ActionName)).Append(',')
              .Append(t.TransitionedBy.ToString()).Append(',')
              .Append(t.TransitionedAt.ToString("O", CultureInfo.InvariantCulture)).Append(',')
              .Append(t.AttachmentCount.ToString(CultureInfo.InvariantCulture)).Append(',')
              .AppendLine(CsvField(t.Comment));
        }

        // UTF-8 BOM — Excel on Windows reads BOM-less UTF-8 as Windows-1252 and mangles Vietnamese
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(sb.ToString())).ToArray();

        var fileName = $"workflow-audit-{dto.InstanceId:N}.csv";
        return new AuditExportResult(AuditExportFormat.Csv, fileName, "text/csv", bytes);
    }

    /// <summary>
    /// RFC 4180 CSV escaping: wrap in quotes if field contains quote, comma, or newline;
    /// escape inner quotes by doubling. Null becomes empty.
    /// </summary>
    private static string CsvField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuote = value.Contains(',') || value.Contains('"')
            || value.Contains('\n') || value.Contains('\r');
        if (!needsQuote) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // ── Dapper projection rows (internal for test access) ───────────────────────

    internal sealed record AuditHeaderRow
    {
        /// <summary>T2 WorkflowInstance internal bigint PK — used for FK sub-query joins.</summary>
        public long InternalInstanceId { get; set; }
        /// <summary>T2 WorkflowInstance PublicId (Guid AK) — surfaced to DTO as external contract.</summary>
        public Guid InstanceId { get; set; }
        public Guid TenantId { get; set; }
        public string DefinitionName { get; set; } = string.Empty;
        public string DefinitionKey { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string CurrentStateName { get; set; } = string.Empty;
        public DateTimeOffset? DueAt { get; set; }
    }

    internal sealed record AuditTransitionRow
    {
        public string FromStateName { get; set; } = string.Empty;
        public string ToStateName { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public Guid TransitionedBy { get; set; }
        public string? Comment { get; set; }
        public DateTimeOffset TransitionedAt { get; set; }
        public int AttachmentCount { get; set; }
    }

    internal sealed record AuditTaskAssignmentRow
    {
        public string TaskTitle { get; set; } = string.Empty;
        public Guid AssignedToId { get; set; }
        public Guid AssignedById { get; set; }
        public string Type { get; set; } = string.Empty;
        public DateTimeOffset AssignedAt { get; set; }
        public string? Reason { get; set; }
    }
}
