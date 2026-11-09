using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// NĐ53 / Law 91/2025: Personal Data Processing (PDP) log.
/// Records WHO processed WHAT PII, WHEN, for WHAT purpose, under WHAT legal basis.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
/// </summary>
public sealed class PersonalDataProcessingLog : IMessageIdentified
{
    private PersonalDataProcessingLog() { }

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>TenantId — stays Guid (FK exception list).</summary>
    public Guid TenantId { get; private set; }

    [DataClassification(DataClassificationLevel.Internal)]
    /// <summary>ProcessorId — stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    public Guid ProcessorId { get; private set; }

    [DataClassification(DataClassificationLevel.Internal)]
    /// <summary>DataSubjectId — stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    public Guid DataSubjectId { get; private set; }

    public string DataCategory { get; private set; } = string.Empty; // e.g. "CCCD", "Phone", "Address"
    public string Purpose { get; private set; } = string.Empty;
    public string LegalBasis { get; private set; } = string.Empty; // e.g. "consent", "legal_obligation"
    public string ProcessingAction { get; private set; } = string.Empty; // collect, use, share, store, delete

    /// <summary>Domain event timestamp — distinct from CreatedAt (T3 PK timestamp).</summary>
    public DateTimeOffset ProcessedAt { get; private set; }

    public static PersonalDataProcessingLog Record(
        IGuidGenerator gen,
        Guid tenantId, Guid processorId, Guid dataSubjectId,
        string dataCategory, string purpose, string legalBasis, string processingAction) =>
        new()
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
            ProcessorId = processorId,
            DataSubjectId = dataSubjectId,
            DataCategory = dataCategory,
            Purpose = purpose,
            LegalBasis = legalBasis,
            ProcessingAction = processingAction
        };
}
