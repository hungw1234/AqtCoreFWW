using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.DeleteProvince;

/// <summary>Command to soft-delete a province by code (sets IsActive = false).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteProvinceCommand(string Code) : ICommand;
