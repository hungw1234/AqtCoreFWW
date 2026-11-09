using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Queries.SimulateRules;

/// <summary>Like EvaluateRules but with trace/explain enabled and no audit event emitted.</summary>
public sealed record SimulateRulesQuery(
    Guid RuleSetId,
    Guid TenantId,
    JsonDocument Input) : IQuery<RuleEvaluationResultDto>;
