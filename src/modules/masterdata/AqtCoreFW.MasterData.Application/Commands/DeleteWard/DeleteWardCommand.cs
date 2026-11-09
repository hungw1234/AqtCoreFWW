using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.DeleteWard;

/// <summary>Command to soft-delete a ward by code (sets IsActive = false).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteWardCommand(string Code) : ICommand;
