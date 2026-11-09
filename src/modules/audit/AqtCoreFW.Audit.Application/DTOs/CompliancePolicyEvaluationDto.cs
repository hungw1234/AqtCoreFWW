using AqtCoreFW.Audit.Domain.Enums;

namespace AqtCoreFW.Audit.Application.DTOs;

/// <summary>Read model for CompliancePolicyEvaluation.</summary>
/// <summary>
/// Read model for CompliancePolicyEvaluation.
/// Id and PolicyId are long (T1 bigint PKs). EntityId stays Guid (polymorphic FK).
/// </summary>
public sealed record CompliancePolicyEvaluationDto(
    long Id,
    long PolicyId,
    string EntityType,
    Guid EntityId,
    EvaluationResult Result,
    string? Details,
    DateTime EvaluatedAt);
