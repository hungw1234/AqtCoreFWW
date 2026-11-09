using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Commands.RegisterUser;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record RegisterUserCommand(
    string FullName,
    string Email,
    string Password,
    string? DepartmentCode,
    Guid? TenantId,
    string? SigningNationalId = null) : ICommand<Guid>;
