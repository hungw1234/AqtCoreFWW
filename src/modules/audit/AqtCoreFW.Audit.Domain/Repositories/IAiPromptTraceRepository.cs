using AqtCoreFW.Audit.Domain.Entities;

namespace AqtCoreFW.Audit.Domain.Repositories;

/// <summary>Append-only repository for AiPromptTrace — no update/delete operations.</summary>
public interface IAiPromptTraceRepository
{
    Task AddAsync(AiPromptTrace trace, CancellationToken cancellationToken = default);
    Task<AiPromptTrace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
