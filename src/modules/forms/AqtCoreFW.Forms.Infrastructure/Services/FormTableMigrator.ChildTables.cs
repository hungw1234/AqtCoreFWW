using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Child-table DDL operations for TableField columns in Table-mode forms.
/// Child table name: forms.SubmTableRows_{templateId:N}_{tableFieldId:N}.
/// Child view name:  forms.v_{sanitizedCode}_{sanitizedFieldKey}.
/// All methods are idempotent — safe to call multiple times.
/// </summary>
public sealed partial class FormTableMigrator
{
    /// <summary>
    /// Creates the child row table for a TableField.
    /// Schema: Id (PK), SubmissionId (FK → parent submissions table), RowIndex, then one typed column per child field.
    /// Idempotent: no-op if table already exists.
    /// </summary>
    public async Task CreateChildTableAsync(
        long templateId,
        FormField tableField,
        IEnumerable<FormField> childFields,
        CancellationToken cancellationToken = default)
    {
        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var childTableName = TableFieldDataRepository.GetChildTableName(templateId, tableField.Id);
        var parentTableName = GetTableName(templateId);

        // Idempotency: check if child table already exists
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('forms') AND name = @name",
            new { name = childTableName }, tx);

        if (exists > 0)
        {
            logger.LogInformation("Child table forms.[{Table}] already exists — skipping CREATE.", childTableName);
            return;
        }

        var columns = new StringBuilder();

        // Typed columns for each active, storable child field
        var storableChildFields = childFields
            .Where(f => f.IsActive && !UiOnlyTypes.Contains(f.Type) && !ComputedTypes.Contains(f.Type)
                        && f.Type != FormFieldType.TableField)  // no nested tables
            .ToList();

        foreach (var field in storableChildFields)
            AppendChildFieldColumns(columns, field);

        var colBlock = columns.Length > 0
            ? columns.ToString().TrimEnd(',', '\r', '\n', ' ') + ","
            : string.Empty;

        // FK constraint name uses short suffix to stay within SQL Server 128-char limit
        var fkName = $"FK_{childTableName}_Sub";
        var idxName = $"IX_{childTableName}_Sub";

        var sql = $"""
            CREATE TABLE forms.[{childTableName}] (
                [Id]           uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID(),
                [SubmissionId] uniqueidentifier NOT NULL,
                [RowIndex]     int              NOT NULL,
                {colBlock}
                CONSTRAINT PK_{childTableName} PRIMARY KEY ([Id]),
                CONSTRAINT {fkName} FOREIGN KEY ([SubmissionId])
                    REFERENCES forms.[{parentTableName}] (SubmissionId)
            );
            CREATE INDEX {idxName} ON forms.[{childTableName}] ([SubmissionId]);
            """;

        logger.LogInformation(
            "Creating child table forms.[{Table}] with {Count} child field columns.",
            childTableName, storableChildFields.Count);

        await conn.ExecuteAsync(sql, transaction: tx);
    }

    /// <summary>
    /// Creates or replaces a BI view joining child rows with FormSubmissions.
    /// View name: forms.v_{sanitizedCode}_{sanitizedFieldKey}.
    /// Idempotent via CREATE OR ALTER VIEW.
    /// </summary>
    public async Task CreateOrReplaceChildViewAsync(
        string templateCode,
        long templateId,
        FormField tableField,
        IEnumerable<FormField> childFields,
        CancellationToken cancellationToken = default)
    {
        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var childTableName = TableFieldDataRepository.GetChildTableName(templateId, tableField.Id);
        var viewName = GetChildViewName(templateCode, tableField.FieldKey);

        var dataColumns = childFields
            .Where(f => f.IsActive && !UiOnlyTypes.Contains(f.Type) && !ComputedTypes.Contains(f.Type)
                        && f.Type != FormFieldType.TableField)
            .SelectMany(f => GetExpandedColumnDefs(f).Select(col => $"    c.[{col.ColName}]"))
            .ToList();

        var colList = dataColumns.Count > 0
            ? ",\n" + string.Join(",\n", dataColumns)
            : string.Empty;

        var sql = $"""
            CREATE OR ALTER VIEW forms.[{viewName}] AS
            SELECT
                c.[SubmissionId],
                c.[RowIndex]{colList},
                s.SubmittedAt,
                s.SubmittedBy
            FROM forms.[{childTableName}] c
            INNER JOIN forms.FormSubmissions s ON s.PublicId = c.[SubmissionId]
            WHERE s.IsDeleted = 0
            """;

        logger.LogInformation("Creating/replacing child view forms.[{View}].", viewName);
        await conn.ExecuteAsync(sql, transaction: tx);
    }

    /// <summary>
    /// ALTER TABLE ADD COLUMN on the child table for a new child field added to an Active template.
    /// Idempotent: no-op if column already exists.
    /// </summary>
    public async Task AddChildColumnAsync(
        long templateId,
        long tableFieldId,
        FormField childField,
        CancellationToken cancellationToken = default)
    {
        if (UiOnlyTypes.Contains(childField.Type) || ComputedTypes.Contains(childField.Type))
            return;

        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var childTableName = TableFieldDataRepository.GetChildTableName(templateId, tableFieldId);

        var additionalCols = GetExpandedColumnDefs(childField);

        logger.LogInformation(
            "Adding {Count} column(s) for child field [{Field}] to forms.[{Table}].",
            additionalCols.Count, childField.FieldKey, childTableName);

        foreach (var (expandedColName, sqlType) in additionalCols)
        {
            var colExists = await conn.ExecuteScalarAsync<int>("""
                SELECT COUNT(*)
                FROM sys.columns c
                INNER JOIN sys.tables t ON t.object_id = c.object_id
                WHERE t.schema_id = SCHEMA_ID('forms')
                  AND t.name = @tableName
                  AND c.name = @colName
                """, new { tableName = childTableName, colName = expandedColName }, tx);

            if (colExists == 0)
            {
                var alterSql = $"ALTER TABLE forms.[{childTableName}] ADD [{expandedColName}] {sqlType} NULL";
                await conn.ExecuteAsync(alterSql, transaction: tx);
            }
        }
    }

    /// <summary>Child view name: v_{sanitizedCode}_{sanitizedFieldKey}.</summary>
    internal static string GetChildViewName(string templateCode, string tableFieldKey) =>
        "v_" + Regex.Replace(templateCode, @"[^a-zA-Z0-9_]", "_")
             + "_" + Regex.Replace(tableFieldKey, @"[^a-zA-Z0-9_]", "_");

    /// <summary>Appends typed column DDL for a child field (single or expanded).</summary>
    private static void AppendChildFieldColumns(StringBuilder sb, FormField field)
    {
        foreach (var (colName, sqlType) in GetExpandedColumnDefs(field))
            sb.AppendLine($"    [{colName}] {sqlType} NULL,");
    }
}
