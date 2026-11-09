using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Files.Domain.Entities;

/// <summary>
/// Defines retention rules for a document type within a tenant.
/// RetainDays: minimum retention period before any action is permitted.
/// ArchiveAfterDays: move to cold storage after this many days (null = never archive).
/// DestroyAfterDays: permanent deletion after this many days (null = never destroy).
/// Invariant: DestroyAfterDays must be >= ArchiveAfterDays when both are set.
/// T1: long PK (DB-assigned, via non-generic AuditableEntity).
/// </summary>
public sealed class RetentionPolicy : AuditableEntity, ITenantScoped
{
    public string Name { get; private set; } = string.Empty;
    public string DocumentType { get; private set; } = string.Empty;
    public int RetainDays { get; private set; }
    public int? ArchiveAfterDays { get; private set; }
    public int? DestroyAfterDays { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>TenantId — stays Guid (FK exception list).</summary>
    public Guid TenantId { get; private set; }

    private RetentionPolicy() { } // EF Core

    public static RetentionPolicy Create(
        string name,
        string documentType,
        int retainDays,
        int? archiveAfterDays,
        int? destroyAfterDays,
        Guid tenantId,
        Guid createdBy)
    {
        var policy = new RetentionPolicy
        {
            Name = name,
            DocumentType = documentType,
            RetainDays = retainDays,
            ArchiveAfterDays = archiveAfterDays,
            DestroyAfterDays = destroyAfterDays,
            IsActive = true,
            TenantId = tenantId
        };
        policy.SetAuditCreate(createdBy);
        return policy;
    }

    public void Update(string name, int retainDays, int? archiveAfterDays, int? destroyAfterDays, Guid modifiedBy)
    {
        Name = name;
        RetainDays = retainDays;
        ArchiveAfterDays = archiveAfterDays;
        DestroyAfterDays = destroyAfterDays;
        SetAuditUpdate(modifiedBy);
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetAuditUpdate(modifiedBy);
    }
}
