using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Dapper-based implementation of ITableFieldDataRepository.
/// Reads/writes child row data for TableField fields in Table-mode forms.
/// Table name: forms.SubmTableRows_{templateId:N}_{tableFieldId:N} (no user input — injection-safe).
/// Shares the same DbConnection + DbTransaction as FormsDbContext.
/// </summary>
public sealed class TableFieldDataRepository(FormsDbContext dbContext) : ITableFieldDataRepository
{
    private const int MaxRowsPerSubmission = 1000;

    /// <summary>
    /// Returns the child table name for a given template + table field combination.
    /// Format: SubmTableRows_{templateId}_{tableFieldId} — long integers, no user input, injection-safe.
    /// </summary>
    internal static string GetChildTableName(long templateId, long tableFieldId) =>
        $"SubmTableRows_{templateId}_{tableFieldId}";

    public async Task InsertRowsAsync(
        long templateId,
        long tableFieldId,
        Guid submissionId,
        IReadOnlyList<IDictionary<string, object?>> rows,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0) return;

        if (rows.Count > MaxRowsPerSubmission)
            throw new InvalidOperationException(
                $"TableField rows exceeded maximum of {MaxRowsPerSubmission} per submission.");

        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var tableName = GetChildTableName(templateId, tableFieldId);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            var colNames = new List<string> { "[Id]", "[SubmissionId]", "[RowIndex]" };
            var paramNames = new List<string> { "@p_Id", "@p_SubmissionId", "@p_RowIndex" };

            var parameters = new DynamicParameters();
            parameters.Add("p_Id", Guid.NewGuid());
            parameters.Add("p_SubmissionId", submissionId);
            parameters.Add("p_RowIndex", i);

            foreach (var (key, value) in row)
            {
                // Reuse the same sanitization as FormTableMigrator — whitelist alphanumeric + underscore
                var sanitized = FormTableMigrator.SanitizeColumnName(key);
                var paramName = $"p_{sanitized}";
                colNames.Add($"[{sanitized}]");
                paramNames.Add($"@{paramName}");
                parameters.Add(paramName, value);
            }

            var sql = $"INSERT INTO forms.[{tableName}] ({string.Join(", ", colNames)}) VALUES ({string.Join(", ", paramNames)})";
            await conn.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: tx, cancellationToken: cancellationToken));
        }
    }

    public async Task<IReadOnlyList<IDictionary<string, object?>>> GetRowsAsync(
        long templateId,
        long tableFieldId,
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        var conn = dbContext.Database.GetDbConnection();
        var tx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var tableName = GetChildTableName(templateId, tableFieldId);

        var sql = $"SELECT * FROM forms.[{tableName}] WHERE SubmissionId = @submissionId ORDER BY RowIndex";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { submissionId }, transaction: tx, cancellationToken: cancellationToken));

        return rows
            .Select(r => (IDictionary<string, object?>)r)
            .ToList();
    }
}
