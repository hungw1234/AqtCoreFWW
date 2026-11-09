using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Audit.Infrastructure.Persistence;

/// <summary>
/// Append-only EF repository for AuditLogEntry.
/// Optional <see cref="ICrossModuleUnitOfWork"/> (Slice 1B-2, mirrors FormSubmissionRepository):
/// when active, <see cref="AddAsync"/> enlists <see cref="AuditDbContext"/> onto the ambient
/// connection+transaction first, so the row write and AuditService's outbox enqueue (written via
/// <c>IOutboxWriter</c> on the SAME transaction) commit or roll back together. When no unit of
/// work is supplied or active, behavior is unchanged — a plain per-call SaveChanges.
/// </summary>
public sealed class AuditLogRepository(
    AuditDbContext db,
    ICrossModuleUnitOfWork? unitOfWork = null) : IAuditLogRepository
{
    public async Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (unitOfWork is { IsActive: true })
            await unitOfWork.EnlistAsync(db, cancellationToken);

        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AuditLogEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AuditLogEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.MessageId == id, cancellationToken);

    public Task<AuditLogEntry?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        db.AuditLogEntries.AsNoTracking()
            .OrderByDescending(e => e.SequenceId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task UpdateSignatureAsync(Guid id, string signature, CancellationToken cancellationToken = default)
    {
        // Direct update via ExecuteUpdateAsync — no change tracking, avoids the
        // AuditDbContext append-only guard. The DB-level immutability trigger blocks UPDATE
        // unless SESSION_CONTEXT('audit_mutate_mode') is set. sp_set_session_context is
        // connection-scoped; EF resets it whenever the connection returns to the pool —
        // open the connection here so both statements execute on the SAME physical connection.
        var conn = db.Database.GetDbConnection();
        var openedHere = false;
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            openedHere = true;
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'signature', @read_only=0;",
                cancellationToken);

            await db.AuditLogEntries
                .Where(e => e.MessageId == id && e.HmacSignature == string.Empty)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.HmacSignature, signature), cancellationToken);
        }
        finally
        {
            // Defensive clear: drop bypass key before the connection returns to the pool.
            // SqlClient resets SESSION_CONTEXT on connection-reset by default, but explicit
            // clear protects against Connection Reset=false misconfig and connection-pinning.
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=NULL, @read_only=0;",
                    CancellationToken.None);
            }
            catch
            {
                // Connection already faulted — pool reset will handle cleanup.
            }

            if (openedHere)
                await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Streams signed entries for the given tenant ordered by SequenceId ascending — used for chain verification.
    /// Ordering by SequenceId (signer-assigned) is stable regardless of the original commit order.
    /// </summary>
    public IAsyncEnumerable<AuditLogEntry> GetByTenantOrderedAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        ByTenant(tenantId)
            .Where(e => e.SequenceId > 0)
            .OrderBy(e => e.SequenceId)
            .AsAsyncEnumerable();

    // ── tenant-match helper ─────────────────────────────────────────────────────────────────
    // Explicitly branch on HasValue to avoid the EF nullable-Guid == null trap.
    // Without the branch, EF may generate "TenantId = NULL" which always yields 0 rows in SQL
    // (SQL NULL comparisons require IS NULL, not =). This is the canonical pattern for all
    // nullable FK tenant-scope queries in this repository.

    private IQueryable<AuditLogEntry> ByTenant(Guid? tenantId) =>
        tenantId.HasValue
            ? db.AuditLogEntries.AsNoTracking().Where(e => e.TenantId == tenantId.Value)
            : db.AuditLogEntries.AsNoTracking().Where(e => e.TenantId == null);
}
