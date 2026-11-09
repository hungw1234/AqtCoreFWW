using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Abstractions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// SQL Server implementation of <see cref="IOutboxStore"/>.
/// Uses dedicated ADO.NET connections (no SESSION_CONTEXT — outbox is tenant-agnostic).
/// Schema names are validated upstream by <see cref="OutboxSchemaRegistry"/> so
/// interpolation here is safe.
/// </summary>
public sealed class OutboxStore(
    IOptionsMonitor<DatabaseOptions> dbOptions,
    IOptionsMonitor<OutboxOptions> outboxOptions,
    IGuidGenerator guidGen) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxRow>> ClaimBatchAsync(
        string schema, int batchSize, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);

        // OUTBOX-1-DISP (v2.133p12): single-statement CTE atomic claim-and-update.
        // UPDLOCK + READPAST + ROWLOCK in the CTE SELECT acquires the lock; the
        // UPDATE...OUTPUT in the SAME statement promotes to X-lock and writes
        // ClaimedAtUtc atomically. Two replicas executing this concurrently
        // partition the candidate rows via READPAST — no double-claim.
        //
        // Stale-claim recovery: rows with ClaimedAtUtc older than ClaimTimeout are
        // re-eligible (defends against dispatcher crash post-claim).
        //
        // OUTBOX-DISP-FU1 (v2.133p16-FU): clock-skew note. `staleCutoff` is computed
        // from the C# host clock (DateTimeOffset.UtcNow), while ClaimedAtUtc is written
        // by SQL Server (SYSUTCDATETIME()). Drift between hosts is benign at the default
        // 5-minute ClaimTimeout — even minute-scale skew leaves the recovery window
        // intact. If prod metrics show observable drift (NTP failure / VM clock skew),
        // switch to server-side computation: `WHERE ... ClaimedAtUtc < DATEADD(SECOND,
        // -@timeoutSeconds, SYSUTCDATETIME())` to source both timestamps from one clock.
        var staleCutoff = DateTimeOffset.UtcNow - outboxOptions.CurrentValue.ClaimTimeout;

        var sql = $@"
;WITH cte AS (
    SELECT TOP (@batchSize) Id, EventType, Payload, RetryCount, ClaimedAtUtc
    FROM [{schema}].[OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE ProcessedAtUtc IS NULL
      AND (ClaimedAtUtc IS NULL OR ClaimedAtUtc < @staleCutoff)
    ORDER BY CreatedAtUtc
)
UPDATE cte
SET ClaimedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.Id, inserted.EventType, inserted.Payload, inserted.RetryCount;";

        var rows = await conn.QueryAsync<OutboxRow>(
            new CommandDefinition(sql, new { batchSize, staleCutoff }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task MarkProcessedAsync(string schema, Guid id, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var sql = $@"UPDATE [{schema}].[OutboxMessages]
                     SET ProcessedAtUtc = SYSUTCDATETIME(),
                         Error = NULL,
                         ClaimedAtUtc = NULL
                     WHERE Id = @id";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task MarkRetryAsync(
        string schema, Guid id, int newRetryCount, string error, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // CRITICAL: must clear ClaimedAtUtc, otherwise the row stays gated by the
        // stale-cutoff (5 min default) and won't be retried until then.
        var sql = $@"UPDATE [{schema}].[OutboxMessages]
                     SET RetryCount = @retry,
                         Error = @error,
                         ClaimedAtUtc = NULL
                     WHERE Id = @id";
        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new { retry = newRetryCount, error = Truncate(error, 2000), id },
            cancellationToken: ct));
    }

    public async Task MoveToDeadLetterAsync(
        string schema, OutboxRow row, string reason, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            const string insertDl = @"
                INSERT INTO messaging.dead_letters
                    (Id, MessageType, Payload, FailureReason, OriginalQueue, ReceivedAt, Status)
                VALUES
                    (@id, @type, @payload, @reason, @queue, SYSUTCDATETIME(), 0)";

            await conn.ExecuteAsync(new CommandDefinition(insertDl, new
            {
                id = guidGen.NewIdMonotonic(),
                type = Truncate(row.EventType, 500),
                payload = row.Payload,
                reason = Truncate(reason, 2000),
                queue = $"outbox:{schema}"
            }, transaction: tx, cancellationToken: ct));

            var markProcessed = $@"UPDATE [{schema}].[OutboxMessages]
                                   SET ProcessedAtUtc = SYSUTCDATETIME(),
                                       RetryCount = @retry,
                                       Error = @reason,
                                       ClaimedAtUtc = NULL
                                   WHERE Id = @id";

            await conn.ExecuteAsync(new CommandDefinition(markProcessed, new
            {
                retry = row.RetryCount + 1,
                reason = Truncate(reason, 2000),
                id = row.Id
            }, transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(dbOptions.CurrentValue.ConnectionString);
        try
        {
            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
