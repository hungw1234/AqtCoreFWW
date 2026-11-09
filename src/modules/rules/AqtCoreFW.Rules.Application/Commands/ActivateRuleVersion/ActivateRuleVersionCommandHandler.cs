using AqtCoreFW.Rules.Application.Commands.CreateRuleSet;
using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Commands.ActivateRuleVersion;

public sealed class ActivateRuleVersionCommandHandler(
    IRuleSetRepository repository,
    ICurrentUser currentUser,
    IDomainEventPublisher eventPublisher,
    IRuleEvaluator ruleEvaluator,
    ITenantContext tenantContext,
    IGuidGenerator gen,
    ILogger<ActivateRuleVersionCommandHandler> logger)
    : IRequestHandler<ActivateRuleVersionCommand, Result<RuleSetDto>>
{
    public async Task<Result<RuleSetDto>> Handle(
        ActivateRuleVersionCommand request,
        CancellationToken cancellationToken)
    {
        // request.RuleSetId is the external Guid PublicId (T2 addressing).
        var ruleSet = await repository.GetByPublicIdAsync(request.RuleSetId, cancellationToken);
        if (ruleSet is null)
            return Result.Fail(new NotFoundError($"RuleSet {request.RuleSetId} not found."));

        // Defence-in-depth — unreachable today because RuleSetRepository.GetByPublicIdAsync
        // inline-filters by tenant (Phase 01). Preserves invariant if repo ever
        // gains IgnoreFilter method or admin-scope query path.
        // Admin-bypass here is read-path consistency only. Actual admin update
        // dies at RuleSetRepository.UpdateAsync with InvalidOperationException
        // (strict tenant match, by design — prevents privileged mis-tagging).
        if (ruleSet.TenantId != tenantContext.TenantId && !tenantContext.IsSystemAdmin)
        {
            logger.LogWarning(
                "Cross-tenant RuleSet activate rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}, ruleSetPublicId={RuleSetPublicId}",
                tenantContext.TenantId, ruleSet.TenantId, ruleSet.PublicId);
            return Result.Fail(new ValidationError("Cross-tenant RuleSet activate rejected"));
        }

        // Snapshot current state before incrementing version.
        // Snapshot uses PublicId (Guid AK) for external identity stability across rebuilds.
        var snapshot = JsonSerializer.Serialize(new
        {
            RuleSetId = ruleSet.PublicId,
            ruleSet.Name,
            ruleSet.Version,
            Rules = ruleSet.Rules.Select(r => new
            {
                RuleId = r.PublicId,
                r.Name, r.Expression, r.Priority, r.Enabled, r.FailureAction
            })
        });

        ruleSet.IncrementVersion();
        ruleSet.Activate(gen);

        var version = RuleVersion.Create(
            ruleSet.Id,
            ruleSet.Version,
            snapshot,
            currentUser.UserId);
        version.MarkActivated();
        ruleSet.AddVersion(version);

        await repository.UpdateAsync(ruleSet, cancellationToken);

        // Evict compiled engine across all pods — new version must be recompiled on next EvaluateAsync.
        // IRuleEvaluator is keyed by PublicId (cross-module exposed Guid).
        ruleEvaluator.InvalidateCache(ruleSet.PublicId);

        await eventPublisher.PublishAsync(ruleSet.DomainEvents, cancellationToken);
        ruleSet.ClearDomainEvents();

        return Result.Ok(new RuleSetDto(
            ruleSet.PublicId, ruleSet.TenantId, ruleSet.Name, ruleSet.Description,
            ruleSet.Category, ruleSet.Version, ruleSet.Status.ToString(),
            ruleSet.TargetEntityType, ruleSet.CreatedAt, ruleSet.UpdatedAt,
            ruleSet.Rules.Select(r => CreateRuleSetCommandHandler.MapRuleToDto(r, ruleSet.PublicId)).ToList()));
    }
}
