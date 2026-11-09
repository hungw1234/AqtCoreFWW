using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Workflow.Infrastructure.Services;

/// <summary>
/// Acquires an exclusive SQL Server advisory lock on a source definition ID using sp_getapplock.
/// Resource: <c>wf-migration-src-{sourceDefinitionId}</c> (max 255 chars for SQL Server).
/// LockOwner=Transaction: lock released automatically on transaction commit/rollback.
/// Caller MUST have an ambient transaction open before calling AcquireAsync.
/// Return value: true = lock acquired, false = timeout (another migration holds the lock).
/// </summary>
public sealed class SourceDefinitionLockService(WorkflowDbContext dbContext)
    : ISourceDefinitionLockService
{
    // sp_getapplock return codes: 0=granted, 1=granted after wait, -1=timeout, -2=cancelled, -3=deadlock
    private const int ReturnCodeGranted = 0;
    private const int ReturnCodeGrantedAfterWait = 1;

    public async Task<bool> AcquireAsync(
        Guid sourceDefinitionId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var resource = $"wf-migration-src-{sourceDefinitionId}";
        var timeoutMs = (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);

        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var returnCode = await conn.QuerySingleAsync<int>(
            new CommandDefinition(
                "DECLARE @rc int; EXEC @rc = sp_getapplock @Resource, @LockMode, @LockOwner, @LockTimeout; SELECT @rc;",
                new
                {
                    Resource = resource,
                    LockMode = "Exclusive",
                    LockOwner = "Transaction",
                    LockTimeout = timeoutMs
                },
                cancellationToken: ct));

        return returnCode is ReturnCodeGranted or ReturnCodeGrantedAfterWait;
    }
}
