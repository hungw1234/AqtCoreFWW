using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.MasterData.Domain.Entities;

/// <summary>Province (Tỉnh/Thành phố) — loaded into IMemoryCache NeverRemove at startup.</summary>
public class Province : Entity
{
    public string Code { get; private set; } = default!;
    public string NameVi { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    private Province() { }

    public static Province Create(string code, string nameVi, string nameEn, int sortOrder = 0) =>
        new()
        {
            Code = code,
            NameVi = nameVi,
            NameEn = nameEn,
            IsActive = true,
            SortOrder = sortOrder
        };

    /// <summary>Marks this province as inactive (e.g., merged into another province post-reform).</summary>
    public void Deactivate() => IsActive = false;
}
