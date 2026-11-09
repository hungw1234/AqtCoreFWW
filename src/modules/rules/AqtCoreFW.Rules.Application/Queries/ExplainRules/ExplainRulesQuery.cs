using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Queries.ExplainRules;

/// <summary>Returns a detailed explain tree — which rules fired, why, with full trace.</summary>
public sealed record ExplainRulesQuery(
    Guid RuleSetId,
    Guid TenantId,
    JsonDocument Input) : IQuery<RuleEvaluationResultDto>;
