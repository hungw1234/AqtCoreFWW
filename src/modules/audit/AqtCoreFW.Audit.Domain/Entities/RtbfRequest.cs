using System.ComponentModel.DataAnnotations;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// Right to be Forgotten (RTBF) request — Law 91/2025 Art. 9 compliance.
/// Tracks status of PII anonymization pipeline execution across Identity/Cases/Forms/Audit modules.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
///
/// DEVIATION: workflow lifecycle mutations preserved — StartProcessing/Complete/PartiallyComplete/Reject
/// are necessary state transitions for RTBF pipeline orchestration.
/// ProcessedBy stays Guid (FK exception list: references AspNetUsers.Id).
/// Phase-3 R3 invariant NOT violated (MarkDeleted/Delete not exposed).
/// </summary>
public sealed class RtbfRequest : IMessageIdentified
{
    private RtbfRequest() { }

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>TenantId — stays Guid (FK exception list).</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The user whose data is to be anonymized. DataSubjectId stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    [DataClassification(DataClassificationLevel.Internal)]
    public Guid DataSubjectId { get; private set; }

    /// <summary>
    /// Optional national ID (CCCD/CMND) for locating guest form submissions
    /// where SubmittedBy is Guid.Empty. Required when data subject submitted forms
    /// without an authenticated account.
    /// </summary>
    [DataClassification(DataClassificationLevel.Restricted)]
    public string? CitizenNationalId { get; private set; }

    /// <summary>Denormalized from Identity — populated at creation for search capability.</summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    [MaxLength(256)]
    public string? SubjectEmail { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>SLA deadline — default 30 days from RequestedAt per agency policy.</summary>
    public DateTimeOffset DueBy { get; private set; }

    public RtbfStatus Status { get; private set; } = RtbfStatus.Pending;

    /// <summary>ProcessedBy stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    public Guid? ProcessedBy { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    /// <summary>
    /// JSON log of per-module anonymization results when status is PartiallyCompleted.
    /// Allows safe idempotent retry — completed modules are skipped on re-run.
    /// </summary>
    public string? FailureLog { get; private set; }

    public static RtbfRequest Create(IGuidGenerator gen, Guid tenantId, Guid dataSubjectId, string? citizenNationalId = null, string? subjectEmail = null) =>
        new()
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            RequestedAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
            DataSubjectId = dataSubjectId,
            SubjectEmail = subjectEmail,
            CitizenNationalId = citizenNationalId,
            DueBy = DateTimeOffset.UtcNow.AddDays(30)
        };

    public void StartProcessing() => Status = RtbfStatus.Processing;

    public void Complete(Guid processedBy)
    {
        Status = RtbfStatus.Completed;
        ProcessedBy = processedBy;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Mark partially complete when some modules succeeded and others failed.
    /// failureLog is a human-readable summary of which steps failed and why.
    /// Job is idempotent — operators can safely re-run to complete remaining modules.
    /// </summary>
    public void PartiallyComplete(Guid processedBy, string failureLog)
    {
        Status = RtbfStatus.PartiallyCompleted;
        ProcessedBy = processedBy;
        ProcessedAt = DateTimeOffset.UtcNow;
        FailureLog = failureLog;
    }

    public void Reject(Guid processedBy, string reason)
    {
        Status = RtbfStatus.Rejected;
        ProcessedBy = processedBy;
        ProcessedAt = DateTimeOffset.UtcNow;
        RejectionReason = reason;
    }
}
