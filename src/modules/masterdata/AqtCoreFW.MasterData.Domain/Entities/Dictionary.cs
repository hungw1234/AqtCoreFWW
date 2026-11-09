using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.MasterData.Domain.Entities;

/// <summary>Lifecycle status of a Dictionary.</summary>
public enum DictionaryStatus
{
    Draft = 1,
    Published = 2,
    Archived = 3
}

/// <summary>
/// A named, versioned lookup dictionary (Danh mục).
/// IsSystemDefined=true means it ships with the platform and cannot be deleted.
/// Tenant-scoped: each tenant can have its own dictionaries plus shared system ones.
/// </summary>
public class Dictionary : AuditableEntity, ITenantScoped, IPublicIdentified
{
    /// <summary>Public-facing AK exposed in API URLs/DTOs. AMEND-A13 (2026-05-03) re-tier T1→T2.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>Unique business code within tenant, e.g. "CASE_STATUS", "GENDER".</summary>
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NameVi { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DictionaryStatus Status { get; private set; }
    public int CurrentVersion { get; private set; }
    public Guid TenantId { get; private set; }
    public bool IsSystemDefined { get; private set; }

    // Navigation
    public ICollection<DictionaryItem> Items { get; private set; } = new List<DictionaryItem>();

    private Dictionary() { }

    public static Dictionary Create(
        IGuidGenerator gen,
        string code, string name, string nameVi,
        string? description, Guid tenantId,
        bool isSystemDefined, Guid createdBy)
    {
        var dict = new Dictionary
        {
            PublicId = gen.NewIdRandom(),
            Code = code,
            Name = name,
            NameVi = nameVi,
            Description = description,
            Status = DictionaryStatus.Draft,
            CurrentVersion = 1,
            TenantId = tenantId,
            IsSystemDefined = isSystemDefined
        };
        dict.SetAuditCreate(createdBy);
        return dict;
    }

    public void Update(string name, string nameVi, string? description, Guid modifiedBy)
    {
        Name = name;
        NameVi = nameVi;
        Description = description;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>Transition to Published — increments version, returns new version number.</summary>
    public int Publish(Guid modifiedBy)
    {
        Status = DictionaryStatus.Published;
        CurrentVersion++;
        SetAuditUpdate(modifiedBy);
        return CurrentVersion;
    }

    public void Archive(Guid modifiedBy)
    {
        Status = DictionaryStatus.Archived;
        SetAuditUpdate(modifiedBy);
    }
}
