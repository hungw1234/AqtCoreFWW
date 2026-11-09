using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.MasterData.Domain.Entities;

/// <summary>
/// Administrative unit (Đơn vị hành chính) — tracks VN admin reform successor chains.
/// IsActive=false means merged/dissolved. SuccessorCode points to current active unit.
/// </summary>
public class AdministrativeUnit : Entity
{
    public string Code { get; private set; } = default!;
    public string NameVi { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;

    /// <summary>Administrative level. Post-2025 reorg the tree is 2-level:
    /// 1=Province/City, 2=Ward/commune (the district level was dissolved).
    /// Legacy 3-level data (3=Ward) may still exist on un-reset databases.</summary>
    public int Level { get; private set; }

    public string? ParentCode { get; private set; }

    /// <summary>Code of the active successor after merger/reform. Null if still active.</summary>
    public string? SuccessorCode { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Date when this unit was dissolved (null if still active).</summary>
    public DateTimeOffset? EffectiveTo { get; private set; }

    private AdministrativeUnit() { }

    public static AdministrativeUnit Create(
        string code, string nameVi, string nameEn, int level,
        string? parentCode, string? successorCode, bool isActive,
        DateTimeOffset? effectiveTo = null) =>
        new()
        {
            Code = code,
            NameVi = nameVi,
            NameEn = nameEn,
            Level = level,
            ParentCode = parentCode,
            SuccessorCode = successorCode,
            IsActive = isActive,
            EffectiveTo = effectiveTo
        };
}
