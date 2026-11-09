using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ChangePassword;

/// <summary>Self-service password change — requires current password verification.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword) : ICommand;
