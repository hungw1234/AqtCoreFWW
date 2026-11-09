using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Queries.EvaluateRules;

public sealed record EvaluateRulesQuery(
    Guid RuleSetId,
    Guid TenantId,
    JsonDocument Input) : IQuery<RuleEvaluationResultDto>;
