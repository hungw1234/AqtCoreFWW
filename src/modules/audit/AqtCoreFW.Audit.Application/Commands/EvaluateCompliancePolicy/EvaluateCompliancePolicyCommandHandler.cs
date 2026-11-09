using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Enums;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.EvaluateCompliancePolicy;

/// <summary>
/// Evaluates an entity against a compliance policy and records the result.
/// Evaluation logic: disabled policy always returns Warning; active policy returns Pass by default.
/// Full rule engine integration is a future concern — this records the evaluation event.
/// </summary>
public sealed class EvaluateCompliancePolicyCommandHandler(ICompliancePolicyRepository repository)
    : IRequestHandler<EvaluateCompliancePolicyCommand, Result<CompliancePolicyEvaluationDto>>
{
    public async Task<Result<CompliancePolicyEvaluationDto>> Handle(
        EvaluateCompliancePolicyCommand request,
        CancellationToken cancellationToken)
    {
        var policy = await repository.GetByIdAsync(request.PolicyId, cancellationToken);
        if (policy is null)
            return Result.Fail(new NotFoundError($"CompliancePolicy {request.PolicyId} not found."));

        var (result, details) = Evaluate(policy, request.EntityType, request.EntityId);

        var evaluation = CompliancePolicyEvaluation.Create(
            request.PolicyId,
            request.EntityType,
            request.EntityId,
            result,
            details);

        await repository.AddEvaluationAsync(evaluation, cancellationToken);

        return Result.Ok(new CompliancePolicyEvaluationDto(
            evaluation.Id,
            evaluation.PolicyId,
            evaluation.EntityType,
            evaluation.EntityId,
            evaluation.Result,
            evaluation.Details,
            evaluation.EvaluatedAt));
    }

    /// <summary>
    /// Basic rule evaluation: disabled policy → Warning; enabled → Pass.
    /// Extend this method when a real rule engine is available.
    /// </summary>
    private static (EvaluationResult result, string? details) Evaluate(
        CompliancePolicy policy,
        string entityType,
        Guid entityId)
    {
        if (!policy.IsEnabled)
            return (EvaluationResult.Warning, $"Policy '{policy.Name}' is disabled — evaluation skipped.");

        return (EvaluationResult.Pass, null);
    }
}
