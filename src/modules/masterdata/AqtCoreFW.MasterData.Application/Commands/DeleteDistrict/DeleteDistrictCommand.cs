using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.DeleteDistrict;

/// <summary>Command to soft-delete a district by code (sets IsActive = false).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteDistrictCommand(string Code) : ICommand;
