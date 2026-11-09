using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.MasterData.Domain.Entities;

/// <summary>Job title (Chức danh cán bộ) — tenant-specific; cached via TwoTierCacheService.</summary>
public class JobTitle : Entity
{
    public string Code { get; private set; } = default!;
    public string NameVi { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string? TenantId { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    private JobTitle() { }

    public static JobTitle Create(string code, string nameVi, string nameEn, string? tenantId = null, int sortOrder = 0) =>
        new()
        {
            Code = code,
            NameVi = nameVi,
            NameEn = nameEn,
            TenantId = tenantId,
            IsActive = true,
            SortOrder = sortOrder
        };
}
