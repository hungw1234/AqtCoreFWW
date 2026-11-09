using AqtCoreFW.Audit.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// Human review record for an AI-generated output — M15 AI Governance.
/// Linked to AiPromptTrace via PromptTraceId.
/// Starts as Pending; reviewer calls Submit() to set Decision.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index, used for outbox dedup
///   - DateTime CreatedAt: UTC, set at creation
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
///
/// DEVIATION: Submit() mutation method preserved (review is pending→decided lifecycle).
/// No UpdatedAt tracked — mutation is in-place on domain fields only.
/// Phase-3 R3 invariant NOT violated (MarkDeleted/Delete not exposed).
/// </summary>
public sealed class AiOutputReview : IMessageIdentified
{
    private AiOutputReview() { }

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>FK to AiPromptTrace.InternalId (bigint identity PK).</summary>
    public long PromptTraceId { get; private set; }

    /// <summary>Reviewer UserId — stays Guid (FK exception list: UserId/ReviewerId reference AspNetUsers.Id).</summary>
    public Guid? ReviewerId { get; private set; }

    public ReviewDecision Decision { get; private set; } = ReviewDecision.Pending;
    public string? Reason { get; private set; }
    public DateTime? ReviewedAt { get; private set; }

    public static AiOutputReview Create(IGuidGenerator gen, long promptTraceId)
    {
        return new AiOutputReview
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            PromptTraceId = promptTraceId,
            Decision = ReviewDecision.Pending
        };
    }

    /// <summary>Submits a review decision. Transitions from Pending to any other state.</summary>
    public void Submit(Guid reviewerId, ReviewDecision decision, string? reason)
    {
        ReviewerId = reviewerId;
        Decision = decision;
        Reason = reason;
        ReviewedAt = DateTime.UtcNow;
    }
}
