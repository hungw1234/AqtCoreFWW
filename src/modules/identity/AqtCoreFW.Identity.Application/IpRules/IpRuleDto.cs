namespace AqtCoreFW.Identity.Application.IpRules;

/// <summary>
/// Read-model DTO for IpFilterRule (T1 entity).
/// T1: long Id is the addressing key — no PublicId exposure needed.
/// Returned by ListIpRulesQuery and GetIpRuleQuery.
/// </summary>
public sealed record IpRuleDto(
    long Id,
    Guid? TenantId,
    string Cidr,
    string Description,
    bool IsEnabled,
    bool IsBlock,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
