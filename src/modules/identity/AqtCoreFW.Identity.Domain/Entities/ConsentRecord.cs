using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// PDPL consent record per Law 91/2025/QH15 + Decree 356/2025.
/// Tracks data processing consent, legal basis, and withdrawal.
/// T2: long PK (Id, DB-assigned) + Guid AK (PublicId, set at create via IGuidGenerator v7).
/// </summary>
public sealed class ConsentRecord : AuditableEntity, IPublicIdentified
{
    private ConsentRecord() { }

    /// <summary>Public-facing alternate key (Guid v7 monotonic). Used in API URLs and DTOs.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>Data subject (citizen/officer) who gave consent.</summary>
    [DataClassification(DataClassificationLevel.Internal)]
    public Guid DataSubjectId { get; private set; }

    /// <summary>Purpose of data processing, e.g. "case_management", "audit_log".</summary>
    public string Purpose { get; private set; } = string.Empty;

    /// <summary>Legal basis per Law 91/2025: Consent | LegalObligation | VitalInterest | PublicTask | LegitimateInterest.</summary>
    public string LegalBasis { get; private set; } = string.Empty;

    /// <summary>Type of data subject: "citizen" | "officer" | "organization".</summary>
    public string? DataSubjectType { get; private set; }

    /// <summary>JSON evidence: IP, UserAgent, form context, etc. for audit trail.</summary>
    public string? EvidenceJson { get; private set; }

    public bool IsWithdrawn { get; private set; }
    public DateTime? WithdrawnAt { get; private set; }

    /// <summary>Reason for withdrawal — required by PDPL for audit trail.</summary>
    public string? WithdrawReason { get; private set; }

    public DateTime? ExpiresAt { get; private set; }

    public Guid? TenantId { get; private set; }

    /// <summary>Factory — records a new consent grant.</summary>
    public static ConsentRecord Create(
        IGuidGenerator gen,
        Guid dataSubjectId,
        string purpose,
        string legalBasis,
        string? dataSubjectType,
        string? evidenceJson,
        DateTime? expiresAt,
        Guid? tenantId,
        Guid actorId)
    {
        var record = new ConsentRecord
        {
            PublicId = gen.NewIdMonotonic(),
            DataSubjectId = dataSubjectId,
            Purpose = purpose.Trim(),
            LegalBasis = legalBasis.Trim(),
            DataSubjectType = dataSubjectType?.Trim(),
            EvidenceJson = evidenceJson,
            ExpiresAt = expiresAt,
            TenantId = tenantId,
            IsWithdrawn = false,
        };
        record.SetAuditCreate(actorId);
        return record;
    }

    /// <summary>Records withdrawal of consent — required by PDPL for audit trail.</summary>
    public void Withdraw(string reason, Guid actorId)
    {
        IsWithdrawn = true;
        WithdrawnAt = DateTime.UtcNow;
        WithdrawReason = reason.Trim();
        SetAuditUpdate(actorId);
    }
}
