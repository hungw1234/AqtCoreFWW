using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.MasterData.Domain.Entities;

/// <summary>District (Quận/Huyện) — loaded into IMemoryCache NeverRemove at startup.</summary>
public class District : Entity
{
    public string Code { get; private set; } = default!;
    public string ProvinceCode { get; private set; } = default!;
    public string NameVi { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    private District() { }

    public static District Create(string code, string provinceCode, string nameVi, string nameEn, int sortOrder = 0) =>
        new()
        {
            Code = code,
            ProvinceCode = provinceCode,
            NameVi = nameVi,
            NameEn = nameEn,
            IsActive = true,
            SortOrder = sortOrder
        };
}
