using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.RegularExpressions;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Table-mode IFormDataStorage implementation.
/// Uses Dapper to read/write the dynamic forms.Submissions_{templateId:N} table.
/// Shares the same DbConnection + DbTransaction as FormsDbContext for consistency.
/// Column names sanitized via whitelist regex — SQL injection prevention.
/// </summary>
public sealed class TableFormDataStorage(FormsDbContext dbContext) : IFormDataStorage
{
    public async Task WriteAsync(
        long templateId,
        Guid submissionId,
        Guid tenantId,
        int version,
        IDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var tableName = FormTableMigrator.GetTableName(templateId);

        var columns = new List<string> { "SubmissionId", "TenantId", "FormTemplateVersion" };
        var paramNames = new List<string> { "@SubmissionId", "@TenantId", "@FormTemplateVersion" };

        var parameters = new DynamicParameters();
        parameters.Add("SubmissionId", submissionId);
        parameters.Add("TenantId", tenantId);
        parameters.Add("FormTemplateVersion", version);

        foreach (var (key, value) in values)
        {
            var colName = SanitizeColumnName(key);
            columns.Add($"[{colName}]");
            paramNames.Add($"@{colName}");
            parameters.Add(colName, value);
        }

        var sql = $"""
            INSERT INTO forms.[{tableName}]
            ({string.Join(", ", columns)})
            VALUES ({string.Join(", ", paramNames)})
            """;

        await conn.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: tx, cancellationToken: cancellationToken));
    }

    public async Task<IDictionary<string, object?>?> ReadAsync(
        long templateId,
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var tableName = FormTableMigrator.GetTableName(templateId);

        var sql = $"SELECT * FROM forms.[{tableName}] WHERE SubmissionId = @SubmissionId";

        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { SubmissionId = submissionId }, transaction: tx, cancellationToken: cancellationToken));

        if (row is null) return null;

        // Convert dynamic ExpandoObject to typed dictionary
        var result = new Dictionary<string, object?>();
        var dict = (IDictionary<string, object?>)row;
        foreach (var (k, v) in dict)
            result[k] = v;

        return result;
    }

    public async Task<PagedResult<IDictionary<string, object?>>> QueryAsync(
        long templateId,
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var tableName = FormTableMigrator.GetTableName(templateId);

        var countSql = $"SELECT COUNT(*) FROM forms.[{tableName}] WHERE TenantId = @TenantId";
        var total = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, new { TenantId = tenantId }, transaction: tx, cancellationToken: cancellationToken));

        var dataSql = $"""
            SELECT * FROM forms.[{tableName}]
            WHERE TenantId = @TenantId
            ORDER BY SubmissionId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await conn.QueryAsync<dynamic>(new CommandDefinition(dataSql, new
        {
            TenantId = tenantId,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        }, transaction: tx, cancellationToken: cancellationToken));

        var items = rows
            .Select(row =>
            {
                var dict = (IDictionary<string, object?>)row;
                return (IDictionary<string, object?>)dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            })
            .ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return new PagedResult<IDictionary<string, object?>>(items, total, meta);
    }

    /// <summary>
    /// RTBF: bulk UPDATE each IsPii column to sentinel for the given submission IDs.
    /// Issues one Dapper UPDATE per piiFieldKey — column names are sanitized via
    /// FormTableMigrator.SanitizeColumnName (whitelist regex) to prevent SQL injection.
    /// Idempotent: WHERE clause skips rows already set to sentinel value.
    /// </summary>
    public async Task AnonymizeAsync(
        long templateId,
        IReadOnlyList<Guid> submissionIds,
        IReadOnlyList<string> piiFieldKeys,
        CancellationToken cancellationToken = default)
    {
        if (submissionIds.Count == 0 || piiFieldKeys.Count == 0) return;

        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var tableName = FormTableMigrator.GetTableName(templateId);

        foreach (var fieldKey in piiFieldKeys)
        {
            var colName = SanitizeColumnName(fieldKey);
            var sql = $"""
                UPDATE forms.[{tableName}]
                SET [{colName}] = @sentinel
                WHERE SubmissionId IN @submissionIds
                  AND ([{colName}] IS NULL OR [{colName}] <> @sentinel)
                """;

            await conn.ExecuteAsync(new CommandDefinition(sql,
                new { sentinel = FormsPiiAnonymizer.Sentinel, submissionIds },
                transaction: tx, cancellationToken: cancellationToken));
        }
    }

    private static string SanitizeColumnName(string key) =>
        FormTableMigrator.SanitizeColumnName(key);
}
