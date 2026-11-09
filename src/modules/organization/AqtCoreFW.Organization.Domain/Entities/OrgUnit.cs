using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Organization.Domain.Entities;

/// <summary>
/// Organization unit in Vietnamese GOV hierarchy (Ministry → Department → Division → Team).
/// Uses adjacency list (ParentId) — recursive CTE for tree queries via Dapper.
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid).
///   ParentId: long? (self-ref FK to T2 OrgUnit.Id).
///   SuccessorId: long? (self-ref FK to T2 OrgUnit.Id).
///   TenantId: Guid (FK exception).
/// </summary>
public sealed class OrgUnit : AuditableEntity, IPublicIdentified
{
    public Guid PublicId { get; private set; }
    public long? ParentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public int Level { get; private set; }
    public bool IsActive { get; private set; }
    public Guid TenantId { get; private set; }
    public long? SuccessorId { get; private set; }

    private readonly List<OrgUnit> _children = [];
    public IReadOnlyList<OrgUnit> Children => _children.AsReadOnly();

    private OrgUnit() { }

    public static OrgUnit Create(
        IGuidGenerator gen,
        string name, string nameEn, string code,
        Guid tenantId, long? parentId = null, int level = 1)
    {
        return new OrgUnit
        {
            PublicId = gen.NewIdMonotonic(),
            Name = name,
            NameEn = nameEn,
            Code = code,
            TenantId = tenantId,
            ParentId = parentId,
            Level = level,
            IsActive = true
        };
    }

    public void Update(string name, string nameEn)
    {
        Name = name;
        NameEn = nameEn;
        MarkUpdated();
    }

    public void SetSuccessor(long successorId)
    {
        SuccessorId = successorId;
        MarkUpdated();
    }

    /// <summary>Soft-deactivate. Only call after verifying no active children remain.</summary>
    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }
}
