using System.Text.Json;
using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Application.Jobs;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Reporting.Infrastructure.Jobs;

/// <summary>
/// Hangfire job: fetches ReportExecution → loads ReportDefinition → executes SQL via Dapper →
/// generates Excel/PDF → uploads to storage → marks execution Done or Failed.
/// H-01: AutomaticRetry limited to 3 (default was 10). Idempotency guard skips terminal states.
/// </summary>
[AutomaticRetry(Attempts = 3)]
public sealed class ExecuteReportJob(
    IReportDefinitionRepository definitionRepo,
    IReportExecutionRepository executionRepo,
    IReadDbConnection db,
    IReportExcelGenerator excelGen,
    IReportPdfGenerator pdfGen,
    IReportStorageService storage,
    ILogger<ExecuteReportJob> logger) : IExecuteReportJob
{
    private const int MaxRows = 50_000;
    private const int RowCheckLimit = MaxRows + 1;

    public async Task ExecuteAsync(Guid executionId, CancellationToken cancellationToken)
    {
        // T2: executionId is the PublicId (Guid AK) passed from RunReportCommandHandler
        var execution = await executionRepo.GetByPublicIdAsync(executionId, cancellationToken);
        if (execution is null)
        {
            logger.LogWarning("ReportExecution {ExecutionId} not found — skipping job", executionId);
            return;
        }

        // H-01: Idempotency guard — skip if already in terminal state (retry safety)
        if (execution.Status is ExecutionStatus.Done or ExecutionStatus.Failed)
        {
            logger.LogWarning("Report execution {ExecutionId} already in terminal state {Status}, skipping retry",
                executionId, execution.Status);
            return;
        }

        // T2: ReportDefinitionId is a long FK — resolve via internal PK lookup
        var definition = await definitionRepo.GetByInternalIdAsync(execution.ReportDefinitionId, cancellationToken);
        if (definition is null)
        {
            execution.MarkFailed($"ReportDefinition {execution.ReportDefinitionId} not found.");
            await executionRepo.SaveChangesAsync(cancellationToken);
            return;
        }

        // Mark running
        execution.MarkRunning();
        await executionRepo.SaveChangesAsync(cancellationToken);

        try
        {
            // Defense-in-depth: re-validate SQL before execution
            var dangerousKeyword = SqlValidationHelper.FindDangerousKeyword(definition.SqlTemplate);
            if (dangerousKeyword is not null)
            {
                execution.MarkFailed($"SQL validation failed: dangerous keyword '{dangerousKeyword}'");
                await executionRepo.SaveChangesAsync(cancellationToken);
                return;
            }

            if (!SqlValidationHelper.StartsWithSelect(definition.SqlTemplate))
            {
                execution.MarkFailed("SQL validation failed: must start with SELECT");
                await executionRepo.SaveChangesAsync(cancellationToken);
                return;
            }

            // Parse user parameters — then force-overwrite TenantId from server-side context.
            var userParams = DeserializeParameters(execution.Parameters);
            // E-01: Never trust user-supplied TenantId — always inject from the report definition's TenantId.
            // definition.TenantId is null for system-wide reports; use "system" as sentinel in that case.
            userParams["TenantId"] = definition.TenantId ?? "system";

            // Execute SQL via Dapper — E-03: 60s timeout prevents runaway report queries
            var rawRows = await db.QueryAsync<dynamic>(definition.SqlTemplate, userParams, cancellationToken, commandTimeout: 60);
            var rows = rawRows
                .Select(r => (IDictionary<string, object?>)r)
                .ToList();

            // Enforce row limit
            if (rows.Count > MaxRows)
            {
                execution.MarkFailed($"Row count {rows.Count} exceeds maximum of {MaxRows}.");
                await executionRepo.SaveChangesAsync(cancellationToken);
                return;
            }

            // Determine column names from first row (or empty)
            var columnNames = rows.Count > 0
                ? rows[0].Keys.ToList().AsReadOnly()
                : new List<string>().AsReadOnly();

            var metadata = new ReportMetadata(
                TenantId: userParams.ContainsKey("TenantId") ? userParams["TenantId"]?.ToString() ?? "" : "",
                FromDate: TryParseDate(userParams, "FromDate"),
                ToDate: TryParseDate(userParams, "ToDate"),
                GeneratedAt: DateTimeOffset.UtcNow);

            // Generate file based on output format
            var format = definition.OutputFormat;
            byte[] fileBytes;
            string extension;

            if (format == OutputFormat.Excel)
            {
                fileBytes = await excelGen.GenerateAsync(
                    definition.Name, definition.NameVi, columnNames, rows, metadata, cancellationToken);
                extension = "xlsx";
            }
            else
            {
                // PDF or Both — generate PDF (for Both, could zip; keeping simple for now)
                fileBytes = await pdfGen.GenerateAsync(
                    definition.Name, definition.NameVi, columnNames, rows, metadata, cancellationToken);
                extension = "pdf";
            }

            // Upload to storage
            var fileName = $"{definition.Name}_{executionId:N}.{extension}";
            var tenantId = userParams.ContainsKey("TenantId") ? userParams["TenantId"]?.ToString() ?? "system" : "system";
            var filePath = await storage.UploadAsync(fileBytes, fileName, tenantId, cancellationToken);

            // Mark done
            execution.MarkDone(filePath);
            await executionRepo.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Report execution {ExecutionId} completed — {RowCount} rows, {FileSize} bytes",
                executionId, rows.Count, fileBytes.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Report execution {ExecutionId} failed", executionId);
            execution.MarkFailed(ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message);
            await executionRepo.SaveChangesAsync(cancellationToken);
        }
    }

    private static Dictionary<string, object?> DeserializeParameters(string parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson) || parametersJson == "{}")
            return new Dictionary<string, object?>();

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson);
            if (dict is null) return new Dictionary<string, object?>();

            return dict.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertJsonElement(kvp.Value));
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };

    private static DateTimeOffset? TryParseDate(Dictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null) return null;
        return DateTimeOffset.TryParse(value.ToString(), out var dto) ? dto : null;
    }
}
