using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.Rules.Domain.Events;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Queries.EvaluateRules;

public sealed class EvaluateRulesQueryHandler(
    IRuleEvaluator evaluator,
    IDomainEventPublisher eventPublisher,
    ITenantContext tenantContext,
    IGuidGenerator gen,
    ILogger<EvaluateRulesQueryHandler> logger)
    : IRequestHandler<EvaluateRulesQuery, Result<RuleEvaluationResultDto>>
{
    public async Task<Result<RuleEvaluationResultDto>> Handle(
        EvaluateRulesQuery request,
        CancellationToken cancellationToken)
    {
        // Defence-in-depth: reject cross-tenant evaluation request.
        // Critical: request.TenantId is used in RuleEvaluatedEvent audit log.
        // Without this guard, an attacker could spoof audit events with a false TenantId.
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant EvaluateRules denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var output = await evaluator.EvaluateAsync(request.RuleSetId, request.Input, cancellationToken);
        sw.Stop();

        // Publish audit event — distinguish from SimulateRules which does not publish
        var inputHash = ComputeHash(request.Input.RootElement.GetRawText());
        var auditEvent = new RuleEvaluatedEvent(
            request.RuleSetId, inputHash, output.IsSuccess, sw.ElapsedMilliseconds, request.TenantId,
            gen.NewIdMonotonic());
        await eventPublisher.PublishAsync([auditEvent], cancellationToken);

        var items = output.Results
            .Select(r => new RuleResultItemDto(r.RuleName, r.IsSuccess, r.ExceptionMessage))
            .ToList<RuleResultItemDto>();

        return Result.Ok(new RuleEvaluationResultDto(
            output.IsSuccess, items, sw.ElapsedMilliseconds, ExplainTrace: null));
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
