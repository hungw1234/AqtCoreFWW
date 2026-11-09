using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.ListPolicyRules;

/// <summary>List policy rules, optionally filtered by tenant and permission code.</summary>
public sealed record ListPolicyRulesQuery(
    Guid? TenantId,
    string? PermissionCode) : IQuery<IReadOnlyList<PolicyRuleDto>>;

public sealed record PolicyRuleDto(
    long Id,
    string Code,
    string PermissionCode,
    string? ConditionExpression,
    string Effect,
    int Priority,
    bool IsActive,
    bool LogOnDeny,
    string? Description,
    Guid? TenantId);
