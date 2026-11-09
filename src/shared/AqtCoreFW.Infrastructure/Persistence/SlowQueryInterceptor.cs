using System.Data.Common;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// EF DbCommandInterceptor — logs queries exceeding the configured threshold as warnings.
/// Phase 02 adds OTEL span tag (db.slow_query=true) and Prometheus counter (db_slow_queries_total).
/// Threshold: Database:SlowQueryThresholdMs (default 100ms; use 1000ms in integration test env).
/// Stateless — registered as singleton.
/// </summary>
public sealed class SlowQueryInterceptor(
    IOptions<DatabaseOptions> dbOptions,
    ILogger<SlowQueryInterceptor> logger) : DbCommandInterceptor
{
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command.CommandText, eventData.Duration);
        return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogIfSlow(command.CommandText, eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override async ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command.CommandText, eventData.Duration);
        return await base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        LogIfSlow(command.CommandText, eventData.Duration);
        return base.ScalarExecuted(command, eventData, result);
    }

    private void LogIfSlow(string sql, TimeSpan duration)
    {
        var thresholdMs = dbOptions.Value.SlowQueryThresholdMs;
        if (duration.TotalMilliseconds <= thresholdMs) return;

        // Truncate SQL for log safety — full query available via OTEL in Phase 02
        var truncatedSql = sql.Length > 500 ? string.Concat(sql.AsSpan(0, 500), "...") : sql;

        logger.LogWarning(
            "Slow query detected ({DurationMs}ms > {ThresholdMs}ms threshold): {Sql}",
            (int)duration.TotalMilliseconds,
            thresholdMs,
            truncatedSql);
    }
}
