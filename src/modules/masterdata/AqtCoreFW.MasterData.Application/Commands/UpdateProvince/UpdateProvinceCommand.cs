using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.UpdateProvince;

/// <summary>Command to update an existing province by code.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateProvinceCommand(
    string Code,
    string NameVi,
    string NameEn,
    int SortOrder) : ICommand;
