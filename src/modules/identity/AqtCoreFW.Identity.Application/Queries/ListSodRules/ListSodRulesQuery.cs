using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.ListSodRules;

/// <summary>List SoD conflict rules, optionally filtered by tenant.</summary>
public sealed record ListSodRulesQuery(Guid? TenantId) : IQuery<IReadOnlyList<SodRuleDto>>;

/// <summary>SoD rule DTO.</summary>
public sealed record SodRuleDto(
    long Id,
    string PermissionCodeA,
    string PermissionCodeB,
    string Severity,
    string? Description,
    bool IsActive,
    Guid? TenantId);
