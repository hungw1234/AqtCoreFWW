using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;

namespace AqtCoreFW.Ai.Domain.Repositories;

/// <summary>Persistence contract for AiPromptTemplate aggregate.</summary>
public interface IAiPromptTemplateRepository
{
    /// <summary>Looks up by public Guid AK — external entry point (controllers, command handlers).</summary>
    Task<AiPromptTemplate?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);

    /// <summary>Looks up by internal long Id — for internal FK lookup paths.</summary>
    Task<AiPromptTemplate?> GetByInternalIdAsync(long internalId, CancellationToken ct = default);

    Task<List<AiPromptTemplate>> GetActiveAsync(PromptCategory? category = null, CancellationToken ct = default);
    Task AddAsync(AiPromptTemplate template, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
