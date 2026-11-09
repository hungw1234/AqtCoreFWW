using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ManageSodRule;

/// <summary>Create a new SoD conflict rule.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record CreateSodRuleCommand(
    string PermissionCodeA,
    string PermissionCodeB,
    string Severity,
    string? Description,
    Guid? TenantId) : ICommand<long>;

/// <summary>Update an existing SoD conflict rule.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record UpdateSodRuleCommand(
    long Id,
    string PermissionCodeA,
    string PermissionCodeB,
    string Severity,
    string? Description,
    bool IsActive) : ICommand;
