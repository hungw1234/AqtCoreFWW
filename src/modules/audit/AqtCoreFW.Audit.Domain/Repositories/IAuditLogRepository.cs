using AqtCoreFW.Audit.Domain.Entities;

namespace AqtCoreFW.Audit.Domain.Repositories;

/// <summary>
/// Append-only repository for AuditLogEntry.
/// No Update/Delete methods — enforcement is in AuditDbContext.
/// </summary>
public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
    Task<AuditLogEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AuditLogEntry?> GetLatestAsync(CancellationToken cancellationToken = default);
    Task UpdateSignatureAsync(Guid id, string signature, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all entries for the given tenant ordered by SequenceId ascending — used for chain verification.
    /// NULL tenant matches rows where TenantId IS NULL.
    /// Only returns rows that have been signed (SequenceId > 0) so verify operates over the committed chain.
    /// </summary>
    IAsyncEnumerable<AuditLogEntry> GetByTenantOrderedAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
