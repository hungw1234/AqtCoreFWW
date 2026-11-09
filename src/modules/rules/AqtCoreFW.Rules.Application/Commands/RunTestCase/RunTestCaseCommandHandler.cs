using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Commands.RunTestCase;

public sealed class RunTestCaseCommandHandler(
    IRuleTestCaseRepository testCaseRepository,
    IRuleSetRepository ruleSetRepository,
    IRuleEvaluator evaluator,
    ITenantContext tenantContext,
    ILogger<RunTestCaseCommandHandler> logger)
    : IRequestHandler<RunTestCaseCommand, Result<RuleEvaluationResultDto>>
{
    public async Task<Result<RuleEvaluationResultDto>> Handle(
        RunTestCaseCommand request,
        CancellationToken cancellationToken)
    {
        // T1 entity: addressed by long internal PK end-to-end.
        var testCase = await testCaseRepository.GetByIdAsync(request.TestCaseId, cancellationToken);
        if (testCase is null)
            return Result.Fail(new NotFoundError($"RuleTestCase {request.TestCaseId} not found."));

        // Defence-in-depth: post-fetch tenant guard.
        // RuleTestCase doesn't have TenantId directly — check parent RuleSet.TenantId.
        // testCase.RuleSetId is internal long FK → use GetByInternalIdAsync.
        var ruleSet = await ruleSetRepository.GetByInternalIdAsync(testCase.RuleSetId, cancellationToken);
        if (ruleSet is null)
            return Result.Fail(new NotFoundError($"RuleSet (internalId={testCase.RuleSetId}) not found."));

        if (!tenantContext.IsSystemAdmin && ruleSet.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant RunTestCase denied: currentTenantId={CurrentTenantId}, ruleSetTenantId={RuleSetTenantId}",
                tenantContext.TenantId, ruleSet.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        using var inputDoc = JsonDocument.Parse(testCase.InputData);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // IRuleEvaluator.EvaluateAsync(Guid ruleSetId) — semantic = PublicId (cross-module exposed).
        var output = await evaluator.EvaluateAsync(ruleSet.PublicId, inputDoc, cancellationToken);
        sw.Stop();

        var resultJson = JsonSerializer.Serialize(new
        {
            output.IsSuccess,
            Results = output.Results.Select(r => new { r.RuleName, r.IsSuccess, r.ExceptionMessage })
        });

        testCase.RecordResult(resultJson, DateTime.UtcNow);
        await testCaseRepository.UpdateAsync(testCase, cancellationToken);

        var items = output.Results
            .Select(r => new RuleResultItemDto(r.RuleName, r.IsSuccess, r.ExceptionMessage))
            .ToList();

        return Result.Ok(new RuleEvaluationResultDto(
            output.IsSuccess, items, sw.ElapsedMilliseconds, ExplainTrace: null));
    }
}
