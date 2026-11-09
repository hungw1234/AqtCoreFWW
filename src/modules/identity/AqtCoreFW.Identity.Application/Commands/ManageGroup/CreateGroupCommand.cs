using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ManageGroup;

/// <summary>Create a new user group (SEC_Group table). Code must be unique per tenant.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record CreateGroupCommand(
    string Code,
    string Name,
    string? Description,
    Guid? TenantId,
    Guid CreatedBy) : ICommand<Guid>;
