using AqtCoreFW.Ai.Domain.Entities;

namespace AqtCoreFW.Ai.Domain.Repositories;

/// <summary>Persistence contract for AI conversation sessions and messages.</summary>
public interface IAiSessionRepository
{
    /// <summary>Looks up by internal long Id (T1 entity — no PublicId AK).</summary>
    Task<AiSession?> GetByIdAsync(long sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<AiSession>> GetByUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task AddAsync(AiSession session, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
