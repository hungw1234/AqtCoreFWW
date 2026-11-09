using AqtCoreFW.Ai.Domain.Entities;

namespace AqtCoreFW.Ai.Domain.Repositories;

/// <summary>Persistence contract for AiModelProfile aggregate (T2 — global resource, no tenant filter).</summary>
public interface IAiModelProfileRepository
{
    /// <summary>Looks up by public Guid AK (external addressing — controller routes, command handlers).</summary>
    Task<AiModelProfile?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);

    /// <summary>Looks up by internal long PK (internal-only FK resolution paths).</summary>
    Task<AiModelProfile?> GetByInternalIdAsync(long internalId, CancellationToken ct = default);

    Task<AiModelProfile?> GetDefaultAsync(CancellationToken ct = default);
    Task<List<AiModelProfile>> GetActiveAsync(CancellationToken ct = default);
    Task AddAsync(AiModelProfile profile, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
