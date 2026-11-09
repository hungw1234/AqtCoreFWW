using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.CreateProvince;

/// <summary>Command to create a new province. Returns the Code (T1 code-business-key — see AMEND-A14 phase-04b/B1 Part 2a).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateProvinceCommand(
    string Code,
    string NameVi,
    string NameEn,
    int SortOrder = 0) : ICommand<string>;
