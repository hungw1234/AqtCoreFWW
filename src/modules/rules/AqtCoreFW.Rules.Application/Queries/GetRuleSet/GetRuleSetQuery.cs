using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Rules.Application.Queries.GetRuleSet;

public sealed record GetRuleSetQuery(Guid RuleSetId, Guid TenantId) : IQuery<RuleSetDto>;
