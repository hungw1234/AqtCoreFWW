using AqtCoreFW.Integration.Domain.Entities;

namespace AqtCoreFW.Integration.Domain.Repositories;

public interface IMessageLogRepository
{
    /// <summary>Look up by T3 MessageId AK (Guid) — external-facing.</summary>
    Task<MessageLog?> GetByMessageIdAsync(Guid messageId, CancellationToken ct);
    /// <summary>Look up by T3 internal PK (long) — internal use only.</summary>
    Task<MessageLog?> GetByInternalIdAsync(long id, CancellationToken ct);
    Task AddAsync(MessageLog log, CancellationToken ct);
    Task UpdateAsync(MessageLog log, CancellationToken ct);
}
