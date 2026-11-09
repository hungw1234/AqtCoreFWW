using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Rules.Application.Queries.ListDecisionTablesByRuleSet;

// List all decision tables belonging to a RuleSet. Flat list — decision tables per
// rule-set are small (single-digit), so pagination would be premature optimisation.
public sealed record ListDecisionTablesByRuleSetQuery(Guid RuleSetId, Guid TenantId)
    : IQuery<IReadOnlyList<DecisionTableDto>>;
