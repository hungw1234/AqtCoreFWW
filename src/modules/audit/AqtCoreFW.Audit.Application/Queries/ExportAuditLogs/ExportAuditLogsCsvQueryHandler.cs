using System.Text;
using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Audit.Application.Queries.ExportAuditLogs;

/// <summary>
/// Builds a CSV (UTF-8 BOM) from up to 10,000 audit log rows.
/// Uses the same Dapper read-side pattern as GetAuditLogsQueryHandler.
/// No string interpolation for user inputs — all via DynamicParameters.
/// Non-SystemAdmin exports always enforce TenantId from ambient context.
/// SystemAdmin exports route through audit_admin_bypass to include NULL-tenant system rows.
/// </summary>
public sealed class ExportAuditLogsCsvQueryHandler(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db,
    ITenantContext tenantContext)
    : IRequestHandler<ExportAuditLogsCsvQuery, Result<byte[]>>
{
    private const int MaxRows = 10_000;

    // CSV header columns — order must match WriteRow()
    private static readonly string CsvHeader =
        "Timestamp,Action,ResourceType,ResourceId,UserId,IpAddress,Details";

    public async Task<Result<byte[]>> Handle(
        ExportAuditLogsCsvQuery request,
        CancellationToken cancellationToken)
    {
        // Enforce tenant isolation: non-SystemAdmin MUST filter by ambient TenantId.
        // Mirrors GetAuditLogsQueryHandler — the controller's ResolveTenantId is a convenience,
        // not a security boundary (the query can be dispatched from other call sites).
        var effectiveTenantId = tenantContext.IsSystemAdmin
            ? request.TenantId  // SystemAdmin can optionally filter by any tenant
            : tenantContext.TenantId
              ?? throw new UnauthorizedAccessException("Missing tenant context for audit log export.");

        var p = new DynamicParameters();
        var where = BuildWhere(request, effectiveTenantId, p);
        p.Add("MaxRows", MaxRows);

        var sql = $"""
            SELECT TOP (@MaxRows)
                   OccurredAt, Action, ResourceType, ResourceId,
                   UserId, IpAddress, CorrelationId
            FROM audit.AuditLogEntries
            {where}
            ORDER BY OccurredAt DESC
            """;

        // RLS: audit_admin_bypass — admin read of system/global audit rows
        var rows = tenantContext.IsSystemAdmin
            ? await db.QueryWithAuditAdminBypassAsync<ExportRow>(sql, p, cancellationToken)
            : await db.QueryAsync<ExportRow>(sql, p, cancellationToken);

        var csv = BuildCsv(rows);
        return Result.Ok(csv);
    }

    // Build CSV bytes with UTF-8 BOM so Excel opens correctly
    private static byte[] BuildCsv(IEnumerable<ExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);

        foreach (var r in rows)
        {
            sb.Append(EscapeCell(r.OccurredAt.ToString("o"))); sb.Append(',');
            sb.Append(EscapeCell(r.Action));                    sb.Append(',');
            sb.Append(EscapeCell(r.ResourceType));              sb.Append(',');
            sb.Append(EscapeCell(r.ResourceId));                sb.Append(',');
            sb.Append(EscapeCell(r.UserId?.ToString()));        sb.Append(',');
            sb.Append(EscapeCell(r.IpAddress));                 sb.Append(',');
            sb.AppendLine(EscapeCell(r.CorrelationId));
        }

        // UTF-8 BOM (EF BB BF) + content
        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[bom.Length + body.Length];
        bom.CopyTo(result, 0);
        body.CopyTo(result, bom.Length);
        return result;
    }

    // CSV-injection + RFC 4180 escaping.
    // 1) Formula-injection guard: a cell whose first char is =, +, -, @, TAB or CR can be
    //    evaluated as a formula by Excel/LibreOffice/Sheets — prefix with a single quote so
    //    the spreadsheet treats it as literal text. (Audit data is system-generated, so this
    //    only triggers on attacker-controlled fields like UserId/IpAddress/ResourceId.)
    // 2) Then wrap in double-quotes if the (possibly-prefixed) cell contains comma, quote, or newline.
    private static string EscapeCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var cell = IsFormulaTrigger(value[0]) ? "'" + value : value;

        if (cell.Contains(',') || cell.Contains('"') || cell.Contains('\n') || cell.Contains('\r'))
            return $"\"{cell.Replace("\"", "\"\"")}\"";
        return cell;
    }

    // Note: this also prefixes plain numeric-ish cells like "-5"/"+84..." — accepted OWASP
    // trade-off; none of the exported columns are numeric so the impact is nil.
    private static bool IsFormulaTrigger(char c) => c is '=' or '+' or '-' or '@' or '\t' or '\r';

    private static string BuildWhere(ExportAuditLogsCsvQuery q, Guid? effectiveTenantId, DynamicParameters p)
    {
        var clauses = new List<string>();

        if (effectiveTenantId.HasValue)             { clauses.Add("TenantId = @TenantId");       p.Add("TenantId", effectiveTenantId); }
        if (q.UserId.HasValue)                      { clauses.Add("UserId = @UserId");             p.Add("UserId", q.UserId); }
        if (q.From.HasValue)                        { clauses.Add("OccurredAt >= @From");          p.Add("From", q.From); }
        if (q.To.HasValue)                          { clauses.Add("OccurredAt <= @To");            p.Add("To", q.To); }
        if (!string.IsNullOrEmpty(q.Action))        { clauses.Add("Action = @Action");             p.Add("Action", q.Action); }
        if (!string.IsNullOrEmpty(q.ModuleName))    { clauses.Add("ModuleName = @ModuleName");     p.Add("ModuleName", q.ModuleName); }
        if (!string.IsNullOrEmpty(q.ResourceType))  { clauses.Add("ResourceType = @ResourceType"); p.Add("ResourceType", q.ResourceType); }
        if (!string.IsNullOrEmpty(q.ResourceId))    { clauses.Add("ResourceId = @ResourceId");     p.Add("ResourceId", q.ResourceId); }

        return clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
    }

    // Lightweight projection — only fields needed for CSV (internal for test visibility)
    internal sealed record ExportRow(
        DateTimeOffset OccurredAt,
        string Action,
        string ResourceType,
        string? ResourceId,
        Guid? UserId,
        string? IpAddress,
        string? CorrelationId);
}
