using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.ListAbacRules;

public sealed record ListAbacRulesQuery(Guid? TenantId) : IQuery<IReadOnlyList<AbacRuleDto>>;

/// <summary>T1: Id is long (DB-assigned IDENTITY, no PublicId AK).</summary>
public sealed record AbacRuleDto(
    long Id, string Resource, string Action,
    string AttributeKey, string AttributeValue,
    string Effect, Guid? TenantId);
