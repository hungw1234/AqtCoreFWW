using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.UpdateWard;

/// <summary>Command to update an existing ward by code.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateWardCommand(
    string Code,
    string DistrictCode,
    string NameVi,
    string NameEn,
    int SortOrder) : ICommand;
