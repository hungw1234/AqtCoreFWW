using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// QĐ 742 mục 3.2.b — configurable retention window for audit log entries.
/// One row = one (tenant, category) pair. Null TenantId = global default;
/// null Category = applies to every category for that tenant. Resolution
/// precedence at purge time: (tenant + category) → (tenant + null) → (null + null).
///
/// Mutable by design (unlike <see cref="AuditLogEntry"/>) — admins update
/// retention windows through the policy UI shipped in Phase 05.
/// T1: long PK (DB-assigned, via non-generic AuditableEntity). IAggregateRoot retains manual event list.
/// </summary>
public sealed class AuditRetentionPolicy : AuditableEntity, IAggregateRoot
{
    /// <summary>Null = global default applied across tenants.</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>Null = applies to every category for the resolved tenant scope.</summary>
    public AuditCategory? Category { get; private set; }

    /// <summary>Hot retention in days. Rows older than this are eligible for archive/purge.</summary>
    public int RetentionDays { get; private set; }

    /// <summary>When true, rows are written to <see cref="ArchivePath"/> before deletion.</summary>
    public bool ArchiveBeforePurge { get; private set; }

    /// <summary>Filesystem or object-storage path used when <see cref="ArchiveBeforePurge"/> is true.</summary>
    public string? ArchivePath { get; private set; }

    /// <summary>Optimistic concurrency token — protects against lost-update races on the policy UI.</summary>
    public byte[] RowVersion { get; private set; } = [];

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    private AuditRetentionPolicy() { }

    public static AuditRetentionPolicy Create(
        Guid? tenantId,
        AuditCategory? category,
        int retentionDays,
        bool archiveBeforePurge = false,
        string? archivePath = null)
    {
        if (retentionDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "RetentionDays must be positive.");
        if (archiveBeforePurge && string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("ArchivePath is required when ArchiveBeforePurge is true.", nameof(archivePath));

        return new AuditRetentionPolicy
        {
            TenantId = tenantId,
            Category = category,
            RetentionDays = retentionDays,
            ArchiveBeforePurge = archiveBeforePurge,
            ArchivePath = archiveBeforePurge ? archivePath : null
        };
    }

    public void UpdateRetention(int retentionDays)
    {
        if (retentionDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "RetentionDays must be positive.");
        RetentionDays = retentionDays;
        MarkUpdated();
    }

    public void EnableArchive(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("ArchivePath cannot be blank.", nameof(archivePath));
        ArchiveBeforePurge = true;
        ArchivePath = archivePath;
        MarkUpdated();
    }

    public void DisableArchive()
    {
        ArchiveBeforePurge = false;
        ArchivePath = null;
        MarkUpdated();
    }
}
