using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Executes idempotent DDL operations for Table-mode form storage.
/// All operations check sys.tables/sys.columns before executing DDL.
/// Column names sanitized via regex whitelist to prevent SQL injection.
/// Table name: forms.Submissions_{templateId:N} (GUID, no hyphens, no user input).
/// View name:  forms.v_{sanitized(code)}.
/// </summary>
public sealed partial class FormTableMigrator(
    FormsDbContext dbContext,
    ILogger<FormTableMigrator> logger) : IFormTableMigrator
{
    // UI-only field types — no column in dynamic table
    private static readonly HashSet<FormFieldType> UiOnlyTypes =
    [
        FormFieldType.Section,
        FormFieldType.Label,
        FormFieldType.Divider
    ];

    // Computed field types — stored differently (Phase 2)
    private static readonly HashSet<FormFieldType> ComputedTypes =
    [
        FormFieldType.Formula
    ];

    public async Task CreateTableAsync(
        long templateId,
        IEnumerable<FormField> fields,
        CancellationToken cancellationToken = default)
    {
        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var tableName = GetTableName(templateId);

        // Idempotency: check if table already exists
        var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('forms') AND name = @name",
            new { name = tableName }, transaction: tx, cancellationToken: cancellationToken));

        if (exists > 0)
        {
            logger.LogInformation("Table forms.[{Table}] already exists — skipping CREATE.", tableName);
            return;
        }

        var columns = new StringBuilder();
        columns.AppendLine("    SubmissionId uniqueidentifier NOT NULL,");
        columns.AppendLine("    TenantId uniqueidentifier NOT NULL,");
        columns.AppendLine("    FormTemplateVersion int NOT NULL,");

        var storableFields = fields
            .Where(f => f.IsActive && !UiOnlyTypes.Contains(f.Type) && !ComputedTypes.Contains(f.Type))
            .ToList();

        foreach (var field in storableFields)
        {
            AppendFieldColumns(columns, field);
        }

        // Build CREATE TABLE — trailing comma removed before PRIMARY KEY
        var colBlock = columns.ToString().TrimEnd(',', '\r', '\n', ' ');
        var sql = $"""
            CREATE TABLE forms.[{tableName}] (
            {colBlock},
                CONSTRAINT PK_{tableName} PRIMARY KEY (SubmissionId)
            )
            """;

        logger.LogInformation("Creating table forms.[{Table}] with {Count} data columns.",
            tableName, storableFields.Count);

        await conn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, cancellationToken: cancellationToken));
    }

    public async Task AddColumnAsync(
        long templateId,
        FormField field,
        CancellationToken cancellationToken = default)
    {
        if (UiOnlyTypes.Contains(field.Type) || ComputedTypes.Contains(field.Type))
            return;

        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var tableName = GetTableName(templateId);
        // Multi-column types: AddressField expands to 3 columns, DateRange to 2
        var additionalCols = GetExpandedColumnDefs(field);

        logger.LogInformation("Adding {Count} column(s) for field [{Field}] to forms.[{Table}].",
            additionalCols.Count, field.FieldKey, tableName);

        foreach (var (expandedColName, sqlType) in additionalCols)
        {
            // Check each expanded column for existence individually
            var colExists = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*)
                FROM sys.columns c
                INNER JOIN sys.tables t ON t.object_id = c.object_id
                WHERE t.schema_id = SCHEMA_ID('forms')
                  AND t.name = @tableName
                  AND c.name = @colName
                """, new { tableName, colName = expandedColName }, transaction: tx, cancellationToken: cancellationToken));

            if (colExists == 0)
            {
                var alterSql = $"ALTER TABLE forms.[{tableName}] ADD [{expandedColName}] {sqlType} NULL";
                await conn.ExecuteAsync(new CommandDefinition(alterSql, transaction: tx, cancellationToken: cancellationToken));
            }
        }
    }

    public async Task CreateOrReplaceViewAsync(
        string code,
        long templateId,
        IEnumerable<FormField> activeFields,
        CancellationToken cancellationToken = default)
    {
        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var tableName = GetTableName(templateId);
        var viewName = GetViewName(code);

        // Build SELECT column list from active data fields — expand multi-column types
        var dataColumns = activeFields
            .Where(f => !UiOnlyTypes.Contains(f.Type) && !ComputedTypes.Contains(f.Type))
            .SelectMany(f => GetExpandedColumnDefs(f).Select(col => $"    t.[{col.ColName}]"))
            .ToList();

        var colList = dataColumns.Count > 0
            ? ",\n" + string.Join(",\n", dataColumns)
            : string.Empty;

        // CREATE OR ALTER VIEW — SQL Server 2016+ syntax.
        // FormSubmission is a T2 entity: Id is the bigint internal PK, PublicId is the Guid external
        // AK. The dynamic table's SubmissionId column stores the submission's PublicId (that is what
        // storage.WriteAsync/InsertRowsAsync are keyed on), so the view must join on s.PublicId — not
        // s.Id — or SQL Server raises an operand-type clash (uniqueidentifier vs bigint).
        var sql = $"""
            CREATE OR ALTER VIEW forms.[{viewName}] AS
            SELECT
                s.PublicId AS SubmissionId,
                s.TenantId,
                s.FormTemplateId,
                s.FormTemplateVersion,
                s.SubmittedBy,
                s.SubmittedAt{colList}
            FROM forms.FormSubmissions s
            INNER JOIN forms.[{tableName}] t ON t.SubmissionId = s.PublicId
            WHERE s.IsDeleted = 0
            """;

        logger.LogInformation("Creating/replacing view forms.[{View}].", viewName);
        await conn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, cancellationToken: cancellationToken));
    }

    /// <summary>Table name uses long integer — no user input, injection-safe.</summary>
    internal static string GetTableName(long templateId) => $"Submissions_{templateId}";

    /// <summary>View name uses sanitized Code — only alphanumeric + underscore allowed.</summary>
    internal static string GetViewName(string code) =>
        "v_" + Regex.Replace(code, @"[^a-zA-Z0-9_]", "_");

    /// <summary>
    /// Column name sanitization — whitelist only alphanumeric + underscore, max 128 chars.
    /// Prevents SQL injection in dynamic DDL.
    /// </summary>
    internal static string SanitizeColumnName(string fieldKey)
    {
        var sanitized = Regex.Replace(fieldKey, @"[^a-zA-Z0-9_]", "_");
        if (sanitized.Length > 128) sanitized = sanitized[..128];

        // Defense-in-depth: reject empty or digit-leading identifiers
        if (string.IsNullOrEmpty(sanitized) || char.IsDigit(sanitized[0]))
            throw new ArgumentException($"Invalid SQL identifier after sanitization: '{fieldKey}'");

        return sanitized;
    }

    /// <summary>
    /// Returns column definitions for a field.
    /// AddressField expands to 3 columns (_province, _district, _ward).
    /// DateRange expands to 2 columns (_from, _to).
    /// All other storable types produce a single column.
    /// </summary>
    internal static List<(string ColName, string SqlType)> GetExpandedColumnDefs(FormField field)
    {
        var baseKey = SanitizeColumnName(field.FieldKey);

        return field.Type switch
        {
            FormFieldType.AddressField =>
            [
                ($"{baseKey}_province", "nvarchar(200)"),
                ($"{baseKey}_district", "nvarchar(200)"),
                ($"{baseKey}_ward",     "nvarchar(200)")
            ],
            FormFieldType.DateRange =>
            [
                ($"{baseKey}_from", "date"),
                ($"{baseKey}_to",   "date")
            ],
            _ => [(baseKey, MapToSqlType(field.Type))]
        };
    }

    /// <summary>Appends one or more column DDL lines for a field to the StringBuilder.</summary>
    private static void AppendFieldColumns(StringBuilder sb, FormField field)
    {
        foreach (var (colName, sqlType) in GetExpandedColumnDefs(field))
            sb.AppendLine($"    [{colName}] {sqlType} NULL,");
    }

    private static string MapToSqlType(FormFieldType type) => type switch
    {
        FormFieldType.Text       => "nvarchar(2000)",
        FormFieldType.Textarea   => "nvarchar(max)",
        FormFieldType.RichText   => "nvarchar(max)",
        FormFieldType.Number     => "decimal(18,4)",
        FormFieldType.Date       => "date",
        FormFieldType.Boolean    => "bit",
        FormFieldType.File
            or FormFieldType.Signature  => "uniqueidentifier",
        // Phase 2 — reference types
        FormFieldType.EnumRef    => "nvarchar(50)",
        FormFieldType.InternalRef => "uniqueidentifier",
        FormFieldType.ExternalRef => "nvarchar(100)",
        // Phase 2 — complex/computed/UI-only handled specially (multi-column or skipped)
        _ => "nvarchar(2000)"
    };
}
