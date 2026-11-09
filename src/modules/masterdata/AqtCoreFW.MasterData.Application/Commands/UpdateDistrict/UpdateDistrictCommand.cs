using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.UpdateDistrict;

/// <summary>Command to update an existing district by code.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateDistrictCommand(
    string Code,
    string ProvinceCode,
    string NameVi,
    string NameEn,
    int SortOrder) : ICommand;
