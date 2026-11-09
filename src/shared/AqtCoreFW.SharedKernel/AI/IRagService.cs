using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Retrieval-Augmented Generation — semantic search over indexed documents.
/// Qdrant-backed: per-tenant collection vectors_{tenantId}.
/// score_threshold=0.7, limit=5, payload index on tenant_id.
/// </summary>
public interface IRagService
{
    Task<Result<string>> QueryAsync(string question, AiContext ctx, CancellationToken ct = default);
}
