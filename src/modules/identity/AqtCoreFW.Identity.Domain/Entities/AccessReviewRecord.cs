using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Access review record per QĐ742 — Hangfire 6-month cron creates pending entries.
/// T2: long PK (Id, DB-assigned) + Guid AK (PublicId, set at create via IGuidGenerator v7).
/// </summary>
public sealed class AccessReviewRecord : AuditableEntity, IPublicIdentified
{
    private AccessReviewRecord() { }

    /// <summary>Public-facing alternate key (Guid v7 monotonic). Used in API URLs and DTOs.</summary>
    public Guid PublicId { get; private set; }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public AccessReviewDecision? Decision { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public DateTime NextReviewDue { get; private set; }

    /// <summary>
    /// Factory — creates a pending access review entry (called by Hangfire cron).
    /// </summary>
    public static AccessReviewRecord Create(
        IGuidGenerator gen,
        Guid userId,
        Guid roleId,
        DateTime nextReviewDue,
        Guid actorId)
    {
        var record = new AccessReviewRecord
        {
            PublicId = gen.NewIdMonotonic(),
            UserId = userId,
            RoleId = roleId,
            NextReviewDue = nextReviewDue,
        };
        record.SetAuditCreate(actorId);
        return record;
    }

    /// <summary>Records a review decision (Retain or Revoke).</summary>
    public void RecordDecision(AccessReviewDecision decision, Guid reviewedBy)
    {
        Decision = decision;
        ReviewedBy = reviewedBy;
        ReviewedAt = DateTime.UtcNow;
        SetAuditUpdate(reviewedBy);
    }
}

public enum AccessReviewDecision
{
    Retain = 1,
    Revoke = 2
}
