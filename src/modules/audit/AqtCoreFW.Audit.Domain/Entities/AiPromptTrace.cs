using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// Append-only record of a single AI prompt/response cycle — M15 AI Governance.
/// Captures tokens, latency, cost, and classification for GOV compliance audit trail.
/// PromptText is stored encrypted at application level; never exposed in DTOs.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index, used for outbox dedup
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
///   - IAggregateRoot domain events DROPPED: plain-class has no entity base; traces emit no events
/// </summary>
public sealed class AiPromptTrace : IMessageIdentified
{
    private AiPromptTrace() { }

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>SessionId — Guid identifier for the AI session (not an entity FK, stays Guid).</summary>
    public Guid SessionId { get; private set; }

    public Guid? ModelProfileId { get; private set; }
    public string ModelName { get; private set; } = string.Empty;

    /// <summary>SHA-256 of prompt content — for deduplication without storing raw text.</summary>
    public string PromptHash { get; private set; } = string.Empty;

    /// <summary>Encrypted prompt text — null if encryption is not configured.</summary>
    public string? PromptText { get; private set; }

    public string? ResponseHash { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public int LatencyMs { get; private set; }
    public decimal Cost { get; private set; }

    /// <summary>Data classification per QĐ742: Public | Internal | Confidential | Secret | TopSecret.</summary>
    public string ClassificationLevel { get; private set; } = string.Empty;

    /// <summary>TenantId — stays Guid (FK exception list: TenantId references Tenant.PublicId Guid AK).</summary>
    public Guid TenantId { get; private set; }

    public static AiPromptTrace Create(
        IGuidGenerator gen,
        Guid sessionId,
        string modelName,
        string promptHash,
        int inputTokens,
        int outputTokens,
        int latencyMs,
        decimal cost,
        string classificationLevel,
        Guid tenantId,
        Guid? modelProfileId = null,
        string? promptText = null,
        string? responseHash = null)
    {
        return new AiPromptTrace
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            SessionId = sessionId,
            ModelName = modelName,
            PromptHash = promptHash,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            LatencyMs = latencyMs,
            Cost = cost,
            ClassificationLevel = classificationLevel,
            TenantId = tenantId,
            ModelProfileId = modelProfileId,
            PromptText = promptText,
            ResponseHash = responseHash
        };
    }
}
