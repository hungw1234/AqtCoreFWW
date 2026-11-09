using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.UpdateUser;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record UpdateUserCommand(
    Guid UserId,
    string FullName,
    string? DepartmentCode,
    Guid? OrgUnitId,
    Guid ActorId,
    string? SigningNationalId = null) : ICommand;
