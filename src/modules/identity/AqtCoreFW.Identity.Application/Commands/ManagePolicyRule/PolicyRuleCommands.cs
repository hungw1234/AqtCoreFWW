using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ManagePolicyRule;

/// <summary>Create a new policy rule.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record CreatePolicyRuleCommand(
    string Code,
    string PermissionCode,
    string? ConditionExpression,
    string Effect,
    int Priority,
    bool LogOnDeny,
    string? Description,
    Guid? TenantId) : ICommand<long>;

/// <summary>Update an existing policy rule.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record UpdatePolicyRuleCommand(
    long Id,
    string PermissionCode,
    string? ConditionExpression,
    string Effect,
    int Priority,
    bool IsActive,
    bool LogOnDeny,
    string? Description) : ICommand;

/// <summary>Delete a policy rule by ID.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record DeletePolicyRuleCommand(long Id) : ICommand;
